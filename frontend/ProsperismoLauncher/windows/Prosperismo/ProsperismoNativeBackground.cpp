#include "pch.h"

#include "NativeBackgroundFrameProtocol.h"
#include "ProsperismoNativeBackground.h"
#include "codegen/react/components/ProsperismoShell/ProsperismoNativeBackground.g.h"

#ifdef RNW_NEW_ARCH

#include <AutoDraw.h>
#include <d2d1_1.h>
#include <dxgiformat.h>
#include <winrt/Microsoft.ReactNative.Composition.Experimental.h>

#include <algorithm>
#include <atomic>
#include <cstring>
#include <thread>
#include <vector>

namespace {

using namespace Prosperismo::NativeBackground;
using CompositionContext =
    winrt::Microsoft::ReactNative::Composition::Experimental::ICompositionContext;
using DrawingSurface =
    winrt::Microsoft::ReactNative::Composition::Experimental::IDrawingSurfaceBrush;
using SpriteVisual =
    winrt::Microsoft::ReactNative::Composition::Experimental::ISpriteVisual;

struct FrameSnapshot {
  uint32_t width{};
  uint32_t height{};
  uint32_t stride{};
  long long sequence{};
  std::vector<uint8_t> pixels;
};

bool HeaderIsValid(FrameHeader const &header, size_t mappedBytes) noexcept {
  if (std::memcmp(header.magic, Magic, sizeof(Magic)) != 0 ||
      header.version != Version ||
      header.format != FormatBgra8Premultiplied ||
      header.reserved0 != LayerParticleOverlay ||
      header.width == 0 || header.height == 0 ||
      header.width > MaxDimension || header.height > MaxDimension ||
      header.activeSlot < 0 || header.activeSlot > 1) {
    return false;
  }

  auto minimumStride = static_cast<uint64_t>(header.width) * 4;
  auto expectedBytes = static_cast<uint64_t>(header.stride) * header.height;
  auto totalBytes = static_cast<uint64_t>(sizeof(FrameHeader)) +
      static_cast<uint64_t>(header.slotBytes) * 2;
  return header.stride >= minimumStride &&
      expectedBytes == header.slotBytes &&
      totalBytes <= mappedBytes;
}

bool TryReadLatestFrame(void const *mapped, size_t mappedBytes, FrameSnapshot &snapshot) {
  auto header = static_cast<FrameHeader const *>(mapped);
  if (!HeaderIsValid(*header, mappedBytes)) {
    return false;
  }

  // The mapping is intentionally opened read-only. Aligned 32/64-bit loads
  // are atomic on the x64 target, while InterlockedCompareExchange would
  // still issue a write cycle and fault on a FILE_MAP_READ view.
  MemoryBarrier();
  auto sequenceBefore = header->sequence;
  auto slotBefore = header->activeSlot;
  if (sequenceBefore <= snapshot.sequence || slotBefore < 0 || slotBefore > 1) {
    return false;
  }

  FrameSnapshot candidate;
  candidate.width = header->width;
  candidate.height = header->height;
  candidate.stride = header->stride;
  candidate.sequence = sequenceBefore;
  candidate.pixels.resize(header->slotBytes);
  auto slots = static_cast<uint8_t const *>(mapped) + sizeof(FrameHeader);
  std::memcpy(
      candidate.pixels.data(),
      slots + static_cast<size_t>(slotBefore) * header->slotBytes,
      header->slotBytes);
  MemoryBarrier();

  MemoryBarrier();
  auto sequenceAfter = header->sequence;
  auto slotAfter = header->activeSlot;
  if (sequenceBefore != sequenceAfter || slotBefore != slotAfter) {
    return false;
  }

  snapshot = std::move(candidate);
  return true;
}

struct NativeBackgroundViewState
    : winrt::implements<
          NativeBackgroundViewState,
          winrt::Windows::Foundation::IInspectable>,
      ProsperismoShellSpecs::BaseProsperismoNativeBackground<NativeBackgroundViewState> {

  ~NativeBackgroundViewState() noexcept {
    Stop();
  }

  void Initialize(winrt::Microsoft::ReactNative::ComponentView const &view) noexcept override {
    m_readyEvent.attach(CreateEventW(nullptr, TRUE, FALSE, ReadyEventName));
    try {
      auto compositionView =
          view.as<winrt::Microsoft::ReactNative::Composition::ViewComponentView>();
      m_context = compositionView
                      .as<winrt::Microsoft::ReactNative::Composition::Experimental::IInternalComponentView>()
                      .CompositionContext();
      m_visual = m_context.CreateSpriteVisual();
      m_visual.RelativeSizeWithOffset({0.0f, 0.0f}, {1.0f, 1.0f});
      m_visual.IsVisible(false);
      m_stopEvent.attach(CreateEventW(nullptr, TRUE, FALSE, nullptr));
      m_consumedEvent.attach(CreateEventW(nullptr, FALSE, FALSE, ConsumedEventName));
      auto weak = get_weak();
      m_destroying = view.Destroying([weak](auto const &, auto const &) noexcept {
        if (auto self = weak.get()) {
          self->Stop();
        }
      });
      m_worker = std::thread([this] { Run(); });
      if (m_readyEvent) {
        SetEvent(m_readyEvent.get());
      }
    } catch (...) {
      // Keep the React tree alive and leave the shell base visible.
      m_context = nullptr;
      m_visual = nullptr;
    }
  }

  winrt::Microsoft::UI::Composition::Visual CreateVisual(
      winrt::Microsoft::ReactNative::ComponentView const &view) noexcept override {
    if (!m_visual) {
      return view.as<winrt::Microsoft::ReactNative::Composition::ComponentView>()
          .Compositor()
          .CreateSpriteVisual();
    }
    return winrt::Microsoft::ReactNative::Composition::Experimental::
        MicrosoftCompositionContextHelper::InnerVisual(m_visual);
  }

 private:
  void Stop() noexcept {
    if (m_stopped.exchange(true)) {
      return;
    }
    if (m_stopEvent) {
      SetEvent(m_stopEvent.get());
    }
    if (m_worker.joinable()) {
      m_worker.join();
    }
  }

  void Run() noexcept {
    while (WaitForSingleObject(m_stopEvent.get(), 0) == WAIT_TIMEOUT) {
      winrt::handle frameEvent(OpenEventW(SYNCHRONIZE, FALSE, FrameEventName));
      winrt::handle mapping(OpenFileMappingW(FILE_MAP_READ, FALSE, MappingName));
      if (!frameEvent || !mapping) {
        if (WaitForSingleObject(m_stopEvent.get(), 500) != WAIT_TIMEOUT) {
          return;
        }
        continue;
      }

      void *mapped = MapViewOfFile(mapping.get(), FILE_MAP_READ, 0, 0, 0);
      if (!mapped) {
        if (WaitForSingleObject(m_stopEvent.get(), 500) != WAIT_TIMEOUT) {
          return;
        }
        continue;
      }
      MEMORY_BASIC_INFORMATION memoryInfo{};
      auto mappedBytes = VirtualQuery(mapped, &memoryInfo, sizeof(memoryInfo))
          ? memoryInfo.RegionSize
          : 0;

      FrameSnapshot latest;
      while (!m_stopped.load()) {
        if (TryReadLatestFrame(mapped, mappedBytes, latest)) {
          // Composition drawing-surface interop is agile. Keeping BeginDraw /
          // EndDraw on this worker avoids reposting it through React's JS/UI
          // dispatcher, which is not the surface's drawing owner.
          Draw(latest);
        }
        HANDLE waits[]{m_stopEvent.get(), frameEvent.get()};
        auto result = WaitForMultipleObjects(2, waits, FALSE, INFINITE);
        if (result != WAIT_OBJECT_0 + 1) {
          break;
        }
      }
      UnmapViewOfFile(mapped);
    }
  }

  void Draw(FrameSnapshot const &frame) noexcept {
    try {
      if (!m_surface || m_width != frame.width || m_height != frame.height) {
        m_width = frame.width;
        m_height = frame.height;
        m_surface = m_context.CreateDrawingSurfaceBrush(
            {static_cast<float>(m_width), static_cast<float>(m_height)},
            winrt::Windows::Graphics::DirectX::DirectXPixelFormat::B8G8R8A8UIntNormalized,
            winrt::Windows::Graphics::DirectX::DirectXAlphaMode::Premultiplied);
        m_surface.HorizontalAlignmentRatio(0.5f);
        m_surface.VerticalAlignmentRatio(0.5f);
        m_surface.Stretch(
            winrt::Microsoft::ReactNative::Composition::Experimental::CompositionStretch::UniformToFill);
        m_visual.Brush(m_surface);
      }

      POINT offset{};
      ::Microsoft::ReactNative::Composition::AutoDrawDrawingSurface draw(m_surface, 1.0f, &offset);
      auto target = draw.GetRenderTarget();
      if (!target) {
        return;
      }

      D2D1_BITMAP_PROPERTIES1 properties{};
      properties.pixelFormat = {
          DXGI_FORMAT_B8G8R8A8_UNORM,
          D2D1_ALPHA_MODE_PREMULTIPLIED};
      properties.dpiX = 96.0f;
      properties.dpiY = 96.0f;
      winrt::com_ptr<ID2D1Bitmap1> bitmap;
      winrt::check_hresult(target->CreateBitmap(
          {frame.width, frame.height},
          frame.pixels.data(),
          frame.stride,
          &properties,
          bitmap.put()));
      target->Clear(D2D1_COLOR_F{0.0f, 0.0f, 0.0f, 0.0f});
      target->SetPrimitiveBlend(D2D1_PRIMITIVE_BLEND_ADD);
      D2D1_RECT_F destination{
          static_cast<float>(offset.x),
          static_cast<float>(offset.y),
          static_cast<float>(offset.x + frame.width),
          static_cast<float>(offset.y + frame.height)};
      target->DrawBitmap(
          bitmap.get(),
          &destination,
          1.0f,
          D2D1_INTERPOLATION_MODE_LINEAR);
      m_visual.IsVisible(true);
      if (m_consumedEvent) {
        SetEvent(m_consumedEvent.get());
      }
    } catch (...) {
      // Device loss or a malformed producer frame leaves the shell base visible.
      m_visual.IsVisible(false);
    }
  }

  CompositionContext m_context{nullptr};
  SpriteVisual m_visual{nullptr};
  DrawingSurface m_surface{nullptr};
  winrt::handle m_stopEvent;
  winrt::handle m_readyEvent;
  winrt::handle m_consumedEvent;
  winrt::event_token m_destroying{};
  std::thread m_worker;
  std::atomic_bool m_stopped{false};
  uint32_t m_width{};
  uint32_t m_height{};
};

} // namespace

void RegisterProsperismoNativeBackground(
    winrt::Microsoft::ReactNative::IReactPackageBuilder const &packageBuilder) noexcept {
  auto fabric = packageBuilder.try_as<winrt::Microsoft::ReactNative::IReactPackageBuilderFabric>();
  if (!fabric) {
    return;
  }
  ProsperismoShellSpecs::RegisterProsperismoNativeBackgroundNativeComponent<
      NativeBackgroundViewState>(
      packageBuilder,
      [](winrt::Microsoft::ReactNative::Composition::IReactCompositionViewComponentBuilder const &builder) noexcept {
        builder.SetViewFeatures(
            winrt::Microsoft::ReactNative::Composition::ComponentViewFeatures::Default &
            ~winrt::Microsoft::ReactNative::Composition::ComponentViewFeatures::NativeBorder);
      });
}

#else

void RegisterProsperismoNativeBackground(
    winrt::Microsoft::ReactNative::IReactPackageBuilder const &) noexcept {}

#endif
