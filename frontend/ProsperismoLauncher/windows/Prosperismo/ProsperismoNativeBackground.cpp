#include "pch.h"

#include "NativeBackgroundFrameProtocol.h"
#include "NativeWavePlate.h"
#include "ProsperismoNativeBackground.h"
#include "codegen/react/components/ProsperismoShell/ProsperismoNativeBackground.g.h"

#ifdef RNW_NEW_ARCH

#include <AutoDraw.h>
#include <d2d1_1.h>
#include <dxgiformat.h>
#include <winrt/Microsoft.ReactNative.Composition.Experimental.h>

#include <algorithm>
#include <atomic>
#include <chrono>
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

constexpr uint32_t FirstWaveRenderWidth = 960;
constexpr uint32_t FirstWaveRenderHeight = 540;
constexpr DWORD FirstWaveFrameIntervalMs = 33;

struct FrameSnapshot {
  uint32_t width{};
  uint32_t height{};
  uint32_t stride{};
  long long sequence{};
  std::vector<uint8_t> pixels;
};

void InitializeControlHeader(BackgroundControlHeader *header) noexcept {
  if (!header) {
    return;
  }
  std::memset(header, 0, sizeof(*header));
  std::memcpy(header->magic, ControlMagic, sizeof(ControlMagic));
  header->version = ControlVersion;
  header->headerBytes = sizeof(BackgroundControlHeader);
}

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
          view.try_as<winrt::Microsoft::ReactNative::Composition::ViewComponentView>();
      if (!compositionView) {
        return;
      }
      auto internalView = compositionView.try_as<
          winrt::Microsoft::ReactNative::Composition::Experimental::IInternalComponentView>();
      if (!internalView) {
        return;
      }
      m_context = internalView.CompositionContext();
      if (!m_context) {
        return;
      }
      m_visual = m_context.CreateSpriteVisual();
      m_visual.RelativeSizeWithOffset({0.0f, 0.0f}, {1.0f, 1.0f});
      m_visual.IsVisible(false);
      m_stopEvent.attach(CreateEventW(nullptr, TRUE, FALSE, nullptr));
      m_redrawEvent.attach(CreateEventW(nullptr, FALSE, FALSE, nullptr));
      m_consumedEvent.attach(CreateEventW(nullptr, FALSE, FALSE, ConsumedEventName));
      m_controlChangedEvent.attach(CreateEventW(nullptr, FALSE, FALSE, ControlChangedEventName));
      m_controlMapping.attach(CreateFileMappingW(
          INVALID_HANDLE_VALUE,
          nullptr,
          PAGE_READWRITE,
          0,
          sizeof(BackgroundControlHeader),
          ControlMappingName));
      if (m_controlMapping) {
        m_controlHeader = static_cast<BackgroundControlHeader *>(MapViewOfFile(
            m_controlMapping.get(), FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(BackgroundControlHeader)));
        InitializeControlHeader(m_controlHeader);
        PublishPresentationState(
            m_particleOverlayEnabled.load() ? HomeLayerMask : SettingsLayerMask);
      }
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
    try {
      if (m_visual) {
        return winrt::Microsoft::ReactNative::Composition::Experimental::
            MicrosoftCompositionContextHelper::InnerVisual(m_visual);
      }
      if (auto compositionView =
              view.try_as<winrt::Microsoft::ReactNative::Composition::ComponentView>()) {
        return compositionView.Compositor().CreateSpriteVisual();
      }
    } catch (...) {
      // A missing composition bridge must never tear down the sibling React
      // tree. The component remains a transparent no-op on that host.
    }
    return nullptr;
  }

  void UpdateProps(
      winrt::Microsoft::ReactNative::ComponentView const &view,
      winrt::com_ptr<ProsperismoShellSpecs::ProsperismoNativeBackgroundProps> const &newProps,
      winrt::com_ptr<ProsperismoShellSpecs::ProsperismoNativeBackgroundProps> const &oldProps) noexcept override {
    ProsperismoShellSpecs::BaseProsperismoNativeBackground<
        NativeBackgroundViewState>::UpdateProps(view, newProps, oldProps);
    auto const enabled = !newProps || newProps->particleOverlayEnabled;
    if (m_particleOverlayEnabled.exchange(enabled) != enabled) {
      PublishPresentationState(enabled ? HomeLayerMask : SettingsLayerMask);
      if (m_redrawEvent) {
        SetEvent(m_redrawEvent.get());
      }
    }
  }

 private:
  void Stop() noexcept {
    if (m_stopped.exchange(true)) {
      return;
    }
    PublishPresentationState(SettingsLayerMask);
    if (m_visual) {
      m_visual.IsVisible(false);
    }
    if (m_stopEvent) {
      SetEvent(m_stopEvent.get());
    }
    if (m_worker.joinable()) {
      m_worker.join();
    }
    if (m_controlHeader) {
      UnmapViewOfFile(m_controlHeader);
      m_controlHeader = nullptr;
    }
  }

  void PublishPresentationState(uint32_t layers) noexcept {
    if (!m_controlHeader) {
      return;
    }
    InterlockedIncrement64(&m_controlHeader->sequence);
    InterlockedExchange(&m_controlHeader->layerMask, static_cast<long>(layers));
    LARGE_INTEGER timestamp{};
    QueryPerformanceCounter(&timestamp);
    m_controlHeader->timestampQpc = static_cast<uint64_t>(timestamp.QuadPart);
    MemoryBarrier();
    InterlockedIncrement64(&m_controlHeader->sequence);
    if (m_controlChangedEvent) {
      SetEvent(m_controlChangedEvent.get());
    }
  }

  void Run() noexcept {
    winrt::handle frameEvent;
    winrt::handle mapping;
    void *mapped{};
    size_t mappedBytes{};
    FrameSnapshot latest;

    while (WaitForSingleObject(m_stopEvent.get(), 0) == WAIT_TIMEOUT) {
      if (!mapped) {
        frameEvent.attach(OpenEventW(SYNCHRONIZE, FALSE, FrameEventName));
        mapping.attach(OpenFileMappingW(FILE_MAP_READ, FALSE, MappingName));
        if (frameEvent && mapping) {
          mapped = MapViewOfFile(mapping.get(), FILE_MAP_READ, 0, 0, 0);
          if (mapped) {
            MEMORY_BASIC_INFORMATION memoryInfo{};
            mappedBytes = VirtualQuery(mapped, &memoryInfo, sizeof(memoryInfo))
                ? memoryInfo.RegionSize
                : 0;
          }
        }
      }

      if (mapped) {
        TryReadLatestFrame(mapped, mappedBytes, latest);
      }

      // Composition drawing-surface interop is agile. Keeping BeginDraw /
      // EndDraw on this worker avoids reposting it through React's JS/UI
      // dispatcher, which is not the surface's drawing owner.
      Draw(latest.sequence > 0 ? &latest : nullptr);

      HANDLE waits[]{m_stopEvent.get(), m_redrawEvent.get(), frameEvent.get()};
      auto const waitCount = frameEvent ? 3u : 2u;
      auto result = WaitForMultipleObjects(
          waitCount, waits, FALSE, FirstWaveFrameIntervalMs);
      if (result == WAIT_OBJECT_0) {
        break;
      }
    }

    if (mapped) {
      UnmapViewOfFile(mapped);
    }
  }

  void Draw(FrameSnapshot const *particleFrame) noexcept {
    try {
      if (!m_surface) {
        m_width = FirstWaveRenderWidth;
        m_height = FirstWaveRenderHeight;
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

      // Plane2 advances its integer permutation phase once per rendered shell
      // frame. Keep the recovered 60Hz source clock even though this native
      // composition producer itself refreshes at 30Hz.
      auto const elapsed = std::chrono::duration<float>(
          std::chrono::steady_clock::now() - m_timeOrigin).count();
      auto const sourceFrame = static_cast<std::int64_t>(elapsed * 60.0f);
      auto const baseStride = m_width * 4u;
      m_nativeWavePixels.resize(static_cast<size_t>(baseStride) * m_height);
      if (!Prosperismo::NativeWave::RenderHomePlateBgra8Premultiplied(
              m_nativeWavePixels.data(), m_width, m_height, baseStride, sourceFrame)) {
        return;
      }

      D2D1_BITMAP_PROPERTIES1 properties{};
      properties.pixelFormat = {
          DXGI_FORMAT_B8G8R8A8_UNORM,
          D2D1_ALPHA_MODE_PREMULTIPLIED};
      properties.dpiX = 96.0f;
      properties.dpiY = 96.0f;
      winrt::com_ptr<ID2D1Bitmap1> nativeWaveBitmap;
      winrt::check_hresult(target->CreateBitmap(
          {m_width, m_height},
          m_nativeWavePixels.data(),
          baseStride,
          &properties,
          nativeWaveBitmap.put()));
      target->Clear(D2D1_COLOR_F{0.0f, 0.0f, 0.0f, 0.0f});
      D2D1_RECT_F destination{
          static_cast<float>(offset.x),
          static_cast<float>(offset.y),
          static_cast<float>(offset.x + m_width),
          static_cast<float>(offset.y + m_height)};
      target->SetPrimitiveBlend(D2D1_PRIMITIVE_BLEND_SOURCE_OVER);
      target->DrawBitmap(
          nativeWaveBitmap.get(),
          &destination,
          1.0f,
          D2D1_INTERPOLATION_MODE_LINEAR);

      if (m_particleOverlayEnabled.load() && particleFrame) {
        winrt::com_ptr<ID2D1Bitmap1> particleBitmap;
        winrt::check_hresult(target->CreateBitmap(
            {particleFrame->width, particleFrame->height},
            particleFrame->pixels.data(),
            particleFrame->stride,
            &properties,
            particleBitmap.put()));
        target->SetPrimitiveBlend(D2D1_PRIMITIVE_BLEND_ADD);
        target->DrawBitmap(
            particleBitmap.get(),
            &destination,
            1.0f,
            D2D1_INTERPOLATION_MODE_LINEAR);
      }
      m_visual.IsVisible(true);
      if (particleFrame && m_consumedEvent) {
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
  winrt::handle m_redrawEvent;
  winrt::handle m_readyEvent;
  winrt::handle m_consumedEvent;
  winrt::handle m_controlChangedEvent;
  winrt::handle m_controlMapping;
  BackgroundControlHeader *m_controlHeader{};
  winrt::event_token m_destroying{};
  std::thread m_worker;
  std::atomic_bool m_stopped{false};
  std::atomic_bool m_particleOverlayEnabled{true};
  std::chrono::steady_clock::time_point m_timeOrigin{std::chrono::steady_clock::now()};
  std::vector<uint8_t> m_nativeWavePixels;
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
