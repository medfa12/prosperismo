// Copyright (C) 2026 SharpEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Silk.NET.Core.Native;
using Silk.NET.Vulkan;

namespace SharpEmu.Libs.Presentation;

/// <summary>
/// Long-lived Vulkan host for the recovered BGLayer particle draw. Firmware
/// shaders, textures, pipeline, target, descriptors and readback storage stay
/// resident; only the firmware-authored guest buffers change between frames.
/// </summary>
internal sealed unsafe class Ps5ParticleVulkanSession : IDisposable
{
    private const uint VerticesPerBillboard = 6;
    private readonly Vk _vk;
    private readonly uint _width;
    private readonly uint _height;
    private readonly int _bufferCount;
    private readonly int _drawCapacity;
    private readonly ulong[] _bufferSizes;
    private readonly Silk.NET.Vulkan.Buffer[][] _guestBuffers;
    private readonly DeviceMemory[][] _guestMemories;
    private readonly DescriptorSet[] _descriptorSets;
    private readonly uint _verticesPerDrawUnit;
    private readonly bool _additiveBlend;
    private readonly (float R, float G, float B, float A) _clearColor;
    private readonly Ps5NativeVertexStream? _hostVertexStream;
    private Instance _instance;
    private Device _device;
    private Queue _queue;
    private CommandPool _commandPool;
    private CommandBuffer _commandBuffer;
    private Image _texture0Image;
    private DeviceMemory _texture0Memory;
    private ImageView _texture0View;
    private Image _texture1Image;
    private DeviceMemory _texture1Memory;
    private ImageView _texture1View;
    private Sampler _sampler;
    private Image _targetImage;
    private DeviceMemory _targetMemory;
    private ImageView _targetView;
    private RenderPass _renderPass;
    private Framebuffer _framebuffer;
    private DescriptorSetLayout _setLayout;
    private PipelineLayout _pipelineLayout;
    private ShaderModule _vertexModule;
    private ShaderModule _fragmentModule;
    private ShaderModule _geometryModule;
    private Pipeline _pipeline;
    private DescriptorPool _descriptorPool;
    private Silk.NET.Vulkan.Buffer _readbackBuffer;
    private DeviceMemory _readbackMemory;
    private Silk.NET.Vulkan.Buffer _hostVertexBuffer;
    private DeviceMemory _hostVertexMemory;
    private ulong _readbackBytes;
    private bool _hasGeometry;
    private bool _disposed;

    public Ps5ParticleVulkanSession(
        Ps5NativeParticleResources resources,
        Ps5NativeParticleDraw exemplar,
        int drawCapacity = Ps5NativeParticleComputeRequest.SmallParticleBankCount,
        uint verticesPerDrawUnit = VerticesPerBillboard,
        bool additiveBlend = true,
        (float R, float G, float B, float A)? clearColor = null)
    {
        if (!resources.Particle0.IsValid || !resources.Particle1.IsValid ||
            resources.VertexSpirv.IsEmpty || resources.FragmentSpirv.IsEmpty ||
            (resources.VertexStream is not null && !resources.VertexStream.IsValid) ||
            !exemplar.IsValid || drawCapacity <= 0 || verticesPerDrawUnit == 0)
        {
            throw new ArgumentException("invalid persistent native-particle session inputs");
        }

        _vk = Vk.GetApi();
        _width = checked((uint)exemplar.Width);
        _height = checked((uint)exemplar.Height);
        _bufferCount = exemplar.VertexBuffers.Count;
        _drawCapacity = drawCapacity;
        _bufferSizes = exemplar.VertexBuffers.Select(static buffer => (ulong)buffer.Length).ToArray();
        _guestBuffers = new Silk.NET.Vulkan.Buffer[drawCapacity][];
        _guestMemories = new DeviceMemory[drawCapacity][];
        _descriptorSets = new DescriptorSet[drawCapacity];
        _verticesPerDrawUnit = verticesPerDrawUnit;
        _additiveBlend = additiveBlend;
        _clearColor = clearColor ?? (0.002f, 0.004f, 0.035f, 1.0f);
        _hostVertexStream = resources.VertexStream;

        try
        {
            Create(resources);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public bool Supports(IReadOnlyList<Ps5NativeParticleDraw> draws)
    {
        if (_disposed || draws.Count == 0 || draws.Count > _drawCapacity)
        {
            return false;
        }

        return draws.All(draw =>
            draw.IsValid &&
            draw.Width == _width &&
            draw.Height == _height &&
            draw.VertexBuffers.Count == _bufferCount &&
            draw.VertexBuffers.Select(static buffer => (ulong)buffer.Length)
                .SequenceEqual(_bufferSizes));
    }

    public Ps5NativeParticleFrame Render(IReadOnlyList<Ps5NativeParticleDraw> draws)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Supports(draws))
        {
            throw new ArgumentException("draw sequence does not match the persistent Vulkan session", nameof(draws));
        }

        for (var drawIndex = 0; drawIndex < draws.Count; drawIndex++)
        {
            for (var bufferIndex = 0; bufferIndex < _bufferCount; bufferIndex++)
            {
                Ps5ParticleDrawProbe.UploadMemory(
                    _vk,
                    _device,
                    _guestMemories[drawIndex][bufferIndex],
                    draws[drawIndex].VertexBuffers[bufferIndex].ToArray());
            }
        }

        Ps5ParticleDrawProbe.Check(
            _vk.ResetCommandBuffer(_commandBuffer, 0),
            "vkResetCommandBuffer(persistent draw)");
        var begin = new CommandBufferBeginInfo { SType = StructureType.CommandBufferBeginInfo };
        Ps5ParticleDrawProbe.Check(
            _vk.BeginCommandBuffer(_commandBuffer, in begin),
            "vkBeginCommandBuffer(persistent draw)");

        var clear = new ClearValue();
        clear.Color = new ClearColorValue(
            _clearColor.R,
            _clearColor.G,
            _clearColor.B,
            _clearColor.A);
        var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
        var renderBegin = new RenderPassBeginInfo
        {
            SType = StructureType.RenderPassBeginInfo,
            RenderPass = _renderPass,
            Framebuffer = _framebuffer,
            RenderArea = scissor,
            ClearValueCount = 1,
            PClearValues = &clear,
        };
        _vk.CmdBeginRenderPass(_commandBuffer, in renderBegin, SubpassContents.Inline);
        _vk.CmdBindPipeline(_commandBuffer, PipelineBindPoint.Graphics, _pipeline);
        if (_hostVertexBuffer.Handle != 0)
        {
            ulong offset = 0;
            var hostVertexBuffer = _hostVertexBuffer;
            _vk.CmdBindVertexBuffers(_commandBuffer, 0, 1, in hostVertexBuffer, in offset);
        }
        for (var drawIndex = 0; drawIndex < draws.Count; drawIndex++)
        {
            var requestedViewport = draws[drawIndex].Viewport;
            var viewport = requestedViewport is { } requested
                ? new Viewport(requested.X, requested.Y, requested.Width, requested.Height, 0, 1)
                : new Viewport(0, 0, _width, _height, 0, 1);
            _vk.CmdSetViewport(_commandBuffer, 0, 1, in viewport);
            _vk.CmdBindDescriptorSets(
                _commandBuffer,
                PipelineBindPoint.Graphics,
                _pipelineLayout,
                0,
                1,
                in _descriptorSets[drawIndex],
                0,
                null);
            _vk.CmdDraw(
                _commandBuffer,
                draws[drawIndex].ParticleCount * _verticesPerDrawUnit,
                1,
                0,
                0);
        }
        _vk.CmdEndRenderPass(_commandBuffer);

        var copyRegion = new BufferImageCopy
        {
            ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
            ImageExtent = new Extent3D(_width, _height, 1),
        };
        _vk.CmdCopyImageToBuffer(
            _commandBuffer,
            _targetImage,
            ImageLayout.TransferSrcOptimal,
            _readbackBuffer,
            1,
            in copyRegion);
        var hostBarrier = new MemoryBarrier
        {
            SType = StructureType.MemoryBarrier,
            SrcAccessMask = AccessFlags.TransferWriteBit,
            DstAccessMask = AccessFlags.HostReadBit,
        };
        _vk.CmdPipelineBarrier(
            _commandBuffer,
            PipelineStageFlags.TransferBit,
            PipelineStageFlags.HostBit,
            0,
            1,
            in hostBarrier,
            0,
            null,
            0,
            null);
        Ps5ParticleDrawProbe.Check(
            _vk.EndCommandBuffer(_commandBuffer),
            "vkEndCommandBuffer(persistent draw)");
        var commandBuffer = _commandBuffer;
        var submit = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            CommandBufferCount = 1,
            PCommandBuffers = &commandBuffer,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.QueueSubmit(_queue, 1, in submit, default),
            "vkQueueSubmit(persistent draw)");
        Ps5ParticleDrawProbe.Check(
            _vk.QueueWaitIdle(_queue),
            "vkQueueWaitIdle(persistent draw)");

        void* mapped;
        Ps5ParticleDrawProbe.Check(
            _vk.MapMemory(_device, _readbackMemory, 0, _readbackBytes, 0, &mapped),
            "vkMapMemory(persistent readback)");
        var rgba = new ReadOnlySpan<byte>(mapped, checked((int)_readbackBytes)).ToArray();
        _vk.UnmapMemory(_device, _readbackMemory);
        return new Ps5NativeParticleFrame((int)_width, (int)_height, rgba);
    }

    private void Create(Ps5NativeParticleResources resources)
    {
        var appName = (byte*)SilkMarshal.StringToPtr("SharpEmuPs5ParticleRenderer");
        try
        {
            var appInfo = new ApplicationInfo
            {
                SType = StructureType.ApplicationInfo,
                PApplicationName = appName,
                ApiVersion = Vk.Version13,
            };
            var instanceInfo = new InstanceCreateInfo
            {
                SType = StructureType.InstanceCreateInfo,
                PApplicationInfo = &appInfo,
            };
            Ps5ParticleDrawProbe.Check(
                _vk.CreateInstance(in instanceInfo, null, out _instance),
                "vkCreateInstance(persistent)");
        }
        finally
        {
            SilkMarshal.Free((nint)appName);
        }

        uint physicalCount = 0;
        Ps5ParticleDrawProbe.Check(
            _vk.EnumeratePhysicalDevices(_instance, &physicalCount, null),
            "vkEnumeratePhysicalDevices(persistent count)");
        if (physicalCount == 0)
        {
            throw new InvalidOperationException("no Vulkan device found");
        }

        var physicals = new PhysicalDevice[physicalCount];
        fixed (PhysicalDevice* pPhysicals = physicals)
        {
            Ps5ParticleDrawProbe.Check(
                _vk.EnumeratePhysicalDevices(_instance, &physicalCount, pPhysicals),
                "vkEnumeratePhysicalDevices(persistent)");
        }

        var physical = physicals[0];
        foreach (var candidate in physicals)
        {
            _vk.GetPhysicalDeviceProperties(candidate, out var properties);
            if (properties.DeviceType == PhysicalDeviceType.DiscreteGpu)
            {
                physical = candidate;
                break;
            }
        }

        _vk.GetPhysicalDeviceFeatures(physical, out var supportedFeatures);
        if (!supportedFeatures.ShaderInt64)
        {
            throw new InvalidOperationException("translated firmware shaders require shaderInt64");
        }

        uint familyCount = 0;
        _vk.GetPhysicalDeviceQueueFamilyProperties(physical, &familyCount, null);
        var families = new QueueFamilyProperties[familyCount];
        fixed (QueueFamilyProperties* pFamilies = families)
        {
            _vk.GetPhysicalDeviceQueueFamilyProperties(physical, &familyCount, pFamilies);
        }
        uint? graphicsFamilyFound = null;
        for (uint index = 0; index < familyCount; index++)
        {
            if (families[index].QueueFlags.HasFlag(QueueFlags.GraphicsBit))
            {
                graphicsFamilyFound = index;
                break;
            }
        }
        var graphicsFamily = graphicsFamilyFound ??
            throw new InvalidOperationException("device has no graphics queue");

        var priority = 1f;
        var queueInfo = new DeviceQueueCreateInfo
        {
            SType = StructureType.DeviceQueueCreateInfo,
            QueueFamilyIndex = graphicsFamily,
            QueueCount = 1,
            PQueuePriorities = &priority,
        };
        _hasGeometry = resources.GeometrySpirv.HasValue;
        var enabledFeatures = new PhysicalDeviceFeatures
        {
            ShaderInt64 = true,
            GeometryShader = _hasGeometry && supportedFeatures.GeometryShader,
            VertexPipelineStoresAndAtomics = supportedFeatures.VertexPipelineStoresAndAtomics,
            FragmentStoresAndAtomics = supportedFeatures.FragmentStoresAndAtomics,
        };
        var deviceInfo = new DeviceCreateInfo
        {
            SType = StructureType.DeviceCreateInfo,
            QueueCreateInfoCount = 1,
            PQueueCreateInfos = &queueInfo,
            PEnabledFeatures = &enabledFeatures,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.CreateDevice(physical, in deviceInfo, null, out _device),
            "vkCreateDevice(persistent)");
        _vk.GetDeviceQueue(_device, graphicsFamily, 0, out _queue);
        _vk.GetPhysicalDeviceMemoryProperties(physical, out var memoryProperties);

        var commandPoolInfo = new CommandPoolCreateInfo
        {
            SType = StructureType.CommandPoolCreateInfo,
            QueueFamilyIndex = graphicsFamily,
            Flags = CommandPoolCreateFlags.ResetCommandBufferBit,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.CreateCommandPool(_device, in commandPoolInfo, null, out _commandPool),
            "vkCreateCommandPool(persistent)");
        var commandAllocate = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _commandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.AllocateCommandBuffers(_device, in commandAllocate, out _commandBuffer),
            "vkAllocateCommandBuffers(persistent)");

        for (var drawIndex = 0; drawIndex < _drawCapacity; drawIndex++)
        {
            _guestBuffers[drawIndex] = new Silk.NET.Vulkan.Buffer[_bufferCount];
            _guestMemories[drawIndex] = new DeviceMemory[_bufferCount];
            for (var bufferIndex = 0; bufferIndex < _bufferCount; bufferIndex++)
            {
                Ps5ParticleDrawProbe.CreateBuffer(
                    _vk,
                    _device,
                    memoryProperties,
                    _bufferSizes[bufferIndex],
                    BufferUsageFlags.StorageBufferBit,
                    true,
                    out _guestBuffers[drawIndex][bufferIndex],
                    out _guestMemories[drawIndex][bufferIndex]);
            }
        }

        if (_hostVertexStream is { } vertexStream)
        {
            Ps5ParticleDrawProbe.CreateBuffer(
                _vk,
                _device,
                memoryProperties,
                (ulong)vertexStream.Data.Length,
                BufferUsageFlags.VertexBufferBit,
                true,
                out _hostVertexBuffer,
                out _hostVertexMemory);
            Ps5ParticleDrawProbe.UploadMemory(
                _vk,
                _device,
                _hostVertexMemory,
                vertexStream.Data.ToArray());
        }

        Ps5ParticleDrawProbe.CreateTexture(
            _vk, _device, memoryProperties, _commandBuffer, _queue,
            resources.Particle0.Rgba.ToArray(),
            (uint)resources.Particle0.Width,
            (uint)resources.Particle0.Height,
            out _texture0Image, out _texture0Memory, out _texture0View);
        Ps5ParticleDrawProbe.CreateTexture(
            _vk, _device, memoryProperties, _commandBuffer, _queue,
            resources.Particle1.Rgba.ToArray(),
            (uint)resources.Particle1.Width,
            (uint)resources.Particle1.Height,
            out _texture1Image, out _texture1Memory, out _texture1View);

        var samplerInfo = new SamplerCreateInfo
        {
            SType = StructureType.SamplerCreateInfo,
            MagFilter = Filter.Linear,
            MinFilter = Filter.Linear,
            MipmapMode = SamplerMipmapMode.Nearest,
            AddressModeU = SamplerAddressMode.ClampToEdge,
            AddressModeV = SamplerAddressMode.ClampToEdge,
            AddressModeW = SamplerAddressMode.ClampToEdge,
            MinLod = 0,
            MaxLod = 0,
            MaxAnisotropy = 1,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.CreateSampler(_device, in samplerInfo, null, out _sampler),
            "vkCreateSampler(persistent)");

        Ps5ParticleDrawProbe.CreateImage(
            _vk,
            _device,
            memoryProperties,
            _width,
            _height,
            Format.R8G8B8A8Unorm,
            ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferSrcBit,
            out _targetImage,
            out _targetMemory);
        _targetView = Ps5ParticleDrawProbe.CreateImageView(
            _vk, _device, _targetImage, Format.R8G8B8A8Unorm);
        CreateRenderPassAndTarget();
        CreatePipeline(resources);
        CreateDescriptors();

        _readbackBytes = checked((ulong)_width * _height * 4);
        Ps5ParticleDrawProbe.CreateBuffer(
            _vk,
            _device,
            memoryProperties,
            _readbackBytes,
            BufferUsageFlags.TransferDstBit,
            true,
            out _readbackBuffer,
            out _readbackMemory);
    }

    private void CreateRenderPassAndTarget()
    {
        var attachment = new AttachmentDescription
        {
            Format = Format.R8G8B8A8Unorm,
            Samples = SampleCountFlags.Count1Bit,
            LoadOp = AttachmentLoadOp.Clear,
            StoreOp = AttachmentStoreOp.Store,
            StencilLoadOp = AttachmentLoadOp.DontCare,
            StencilStoreOp = AttachmentStoreOp.DontCare,
            InitialLayout = ImageLayout.Undefined,
            FinalLayout = ImageLayout.TransferSrcOptimal,
        };
        var colorReference = new AttachmentReference
        {
            Attachment = 0,
            Layout = ImageLayout.ColorAttachmentOptimal,
        };
        var subpass = new SubpassDescription
        {
            PipelineBindPoint = PipelineBindPoint.Graphics,
            ColorAttachmentCount = 1,
            PColorAttachments = &colorReference,
        };
        var dependency = new SubpassDependency
        {
            SrcSubpass = Vk.SubpassExternal,
            DstSubpass = 0,
            SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit,
            DstAccessMask = AccessFlags.ColorAttachmentWriteBit,
        };
        var renderPassInfo = new RenderPassCreateInfo
        {
            SType = StructureType.RenderPassCreateInfo,
            AttachmentCount = 1,
            PAttachments = &attachment,
            SubpassCount = 1,
            PSubpasses = &subpass,
            DependencyCount = 1,
            PDependencies = &dependency,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.CreateRenderPass(_device, in renderPassInfo, null, out _renderPass),
            "vkCreateRenderPass(persistent)");
        var targetView = _targetView;
        var framebufferInfo = new FramebufferCreateInfo
        {
            SType = StructureType.FramebufferCreateInfo,
            RenderPass = _renderPass,
            AttachmentCount = 1,
            PAttachments = &targetView,
            Width = _width,
            Height = _height,
            Layers = 1,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.CreateFramebuffer(_device, in framebufferInfo, null, out _framebuffer),
            "vkCreateFramebuffer(persistent)");
    }

    private void CreatePipeline(Ps5NativeParticleResources resources)
    {
        var layoutBindings = stackalloc DescriptorSetLayoutBinding[3];
        layoutBindings[0] = new DescriptorSetLayoutBinding
        {
            Binding = 0,
            DescriptorType = DescriptorType.StorageBuffer,
            DescriptorCount = (uint)_bufferCount,
            StageFlags = ShaderStageFlags.VertexBit | ShaderStageFlags.FragmentBit,
        };
        for (uint binding = 1; binding <= 2; binding++)
        {
            layoutBindings[binding] = new DescriptorSetLayoutBinding
            {
                Binding = binding,
                DescriptorType = DescriptorType.CombinedImageSampler,
                DescriptorCount = 1,
                StageFlags = ShaderStageFlags.FragmentBit,
            };
        }
        var setLayoutInfo = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 3,
            PBindings = layoutBindings,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.CreateDescriptorSetLayout(_device, in setLayoutInfo, null, out _setLayout),
            "vkCreateDescriptorSetLayout(persistent)");
        var setLayout = _setLayout;
        var pipelineLayoutInfo = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = 1,
            PSetLayouts = &setLayout,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.CreatePipelineLayout(_device, in pipelineLayoutInfo, null, out _pipelineLayout),
            "vkCreatePipelineLayout(persistent)");

        _vertexModule = Ps5ParticleDrawProbe.CreateShaderModule(
            _vk, _device, resources.VertexSpirv.ToArray());
        _fragmentModule = Ps5ParticleDrawProbe.CreateShaderModule(
            _vk, _device, resources.FragmentSpirv.ToArray());
        if (_hasGeometry)
        {
            _geometryModule = Ps5ParticleDrawProbe.CreateShaderModule(
                _vk, _device, resources.GeometrySpirv!.Value.ToArray());
        }

        var entryName = (byte*)SilkMarshal.StringToPtr("main");
        try
        {
            var stageCount = _hasGeometry ? 3u : 2u;
            var stages = stackalloc PipelineShaderStageCreateInfo[(int)stageCount];
            stages[0] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.VertexBit,
                Module = _vertexModule,
                PName = entryName,
            };
            var fragmentIndex = _hasGeometry ? 2 : 1;
            if (_hasGeometry)
            {
                stages[1] = new PipelineShaderStageCreateInfo
                {
                    SType = StructureType.PipelineShaderStageCreateInfo,
                    Stage = ShaderStageFlags.GeometryBit,
                    Module = _geometryModule,
                    PName = entryName,
                };
            }
            stages[fragmentIndex] = new PipelineShaderStageCreateInfo
            {
                SType = StructureType.PipelineShaderStageCreateInfo,
                Stage = ShaderStageFlags.FragmentBit,
                Module = _fragmentModule,
                PName = entryName,
            };
            var vertexBinding = new VertexInputBindingDescription
            {
                Binding = 0,
                Stride = _hostVertexStream?.Stride ?? 0,
                InputRate = VertexInputRate.Vertex,
            };
            var attributeCount = _hostVertexStream?.Attributes.Count ?? 0;
            var vertexAttributes = stackalloc VertexInputAttributeDescription[attributeCount];
            for (var attributeIndex = 0; attributeIndex < attributeCount; attributeIndex++)
            {
                var attribute = _hostVertexStream!.Attributes[attributeIndex];
                vertexAttributes[attributeIndex] = new VertexInputAttributeDescription
                {
                    Location = attribute.Location,
                    Binding = 0,
                    Offset = attribute.Offset,
                    Format = attribute.Format switch
                    {
                        Ps5NativeVertexFormat.Float2 => Format.R32G32Sfloat,
                        Ps5NativeVertexFormat.Float3 => Format.R32G32B32Sfloat,
                        Ps5NativeVertexFormat.Float4 => Format.R32G32B32A32Sfloat,
                        _ => throw new ArgumentOutOfRangeException(),
                    },
                };
            }
            var vertexInput = new PipelineVertexInputStateCreateInfo
            {
                SType = StructureType.PipelineVertexInputStateCreateInfo,
                VertexBindingDescriptionCount = attributeCount > 0 ? 1u : 0u,
                PVertexBindingDescriptions = attributeCount > 0 ? &vertexBinding : null,
                VertexAttributeDescriptionCount = (uint)attributeCount,
                PVertexAttributeDescriptions = vertexAttributes,
            };
            var assembly = new PipelineInputAssemblyStateCreateInfo
            {
                SType = StructureType.PipelineInputAssemblyStateCreateInfo,
                Topology = PrimitiveTopology.TriangleList,
            };
            var scissor = new Rect2D(new Offset2D(0, 0), new Extent2D(_width, _height));
            var viewportState = new PipelineViewportStateCreateInfo
            {
                SType = StructureType.PipelineViewportStateCreateInfo,
                ViewportCount = 1,
                ScissorCount = 1,
                PScissors = &scissor,
            };
            var dynamicState = DynamicState.Viewport;
            var dynamicStateInfo = new PipelineDynamicStateCreateInfo
            {
                SType = StructureType.PipelineDynamicStateCreateInfo,
                DynamicStateCount = 1,
                PDynamicStates = &dynamicState,
            };
            var rasterizer = new PipelineRasterizationStateCreateInfo
            {
                SType = StructureType.PipelineRasterizationStateCreateInfo,
                PolygonMode = PolygonMode.Fill,
                CullMode = CullModeFlags.None,
                FrontFace = FrontFace.CounterClockwise,
                LineWidth = 1,
            };
            var multisample = new PipelineMultisampleStateCreateInfo
            {
                SType = StructureType.PipelineMultisampleStateCreateInfo,
                RasterizationSamples = SampleCountFlags.Count1Bit,
            };
            var blendAttachment = new PipelineColorBlendAttachmentState
            {
                BlendEnable = _additiveBlend,
                SrcColorBlendFactor = BlendFactor.One,
                DstColorBlendFactor = BlendFactor.One,
                ColorBlendOp = BlendOp.Add,
                SrcAlphaBlendFactor = BlendFactor.One,
                DstAlphaBlendFactor = BlendFactor.One,
                AlphaBlendOp = BlendOp.Add,
                ColorWriteMask = ColorComponentFlags.RBit | ColorComponentFlags.GBit |
                                 ColorComponentFlags.BBit | ColorComponentFlags.ABit,
            };
            var blendState = new PipelineColorBlendStateCreateInfo
            {
                SType = StructureType.PipelineColorBlendStateCreateInfo,
                AttachmentCount = 1,
                PAttachments = &blendAttachment,
            };
            var graphicsInfo = new GraphicsPipelineCreateInfo
            {
                SType = StructureType.GraphicsPipelineCreateInfo,
                StageCount = stageCount,
                PStages = stages,
                PVertexInputState = &vertexInput,
                PInputAssemblyState = &assembly,
                PViewportState = &viewportState,
                PRasterizationState = &rasterizer,
                PMultisampleState = &multisample,
                PColorBlendState = &blendState,
                PDynamicState = &dynamicStateInfo,
                Layout = _pipelineLayout,
                RenderPass = _renderPass,
                Subpass = 0,
            };
            Ps5ParticleDrawProbe.Check(
                _vk.CreateGraphicsPipelines(
                    _device, default, 1, in graphicsInfo, null, out _pipeline),
                "vkCreateGraphicsPipelines(persistent)");
        }
        finally
        {
            SilkMarshal.Free((nint)entryName);
        }
    }

    private void CreateDescriptors()
    {
        var poolSizes = stackalloc DescriptorPoolSize[2];
        poolSizes[0] = new DescriptorPoolSize(
            DescriptorType.StorageBuffer,
            (uint)(_bufferCount * _drawCapacity));
        poolSizes[1] = new DescriptorPoolSize(
            DescriptorType.CombinedImageSampler,
            (uint)(2 * _drawCapacity));
        var poolInfo = new DescriptorPoolCreateInfo
        {
            SType = StructureType.DescriptorPoolCreateInfo,
            MaxSets = (uint)_drawCapacity,
            PoolSizeCount = 2,
            PPoolSizes = poolSizes,
        };
        Ps5ParticleDrawProbe.Check(
            _vk.CreateDescriptorPool(_device, in poolInfo, null, out _descriptorPool),
            "vkCreateDescriptorPool(persistent)");
        var setLayouts = Enumerable.Repeat(_setLayout, _drawCapacity).ToArray();
        fixed (DescriptorSetLayout* pSetLayouts = setLayouts)
        fixed (DescriptorSet* pDescriptorSets = _descriptorSets)
        {
            var allocateInfo = new DescriptorSetAllocateInfo
            {
                SType = StructureType.DescriptorSetAllocateInfo,
                DescriptorPool = _descriptorPool,
                DescriptorSetCount = (uint)_drawCapacity,
                PSetLayouts = pSetLayouts,
            };
            Ps5ParticleDrawProbe.Check(
                _vk.AllocateDescriptorSets(_device, in allocateInfo, pDescriptorSets),
                "vkAllocateDescriptorSets(persistent)");
        }

        var imageInfos = stackalloc DescriptorImageInfo[2];
        imageInfos[0] = new DescriptorImageInfo(
            _sampler, _texture0View, ImageLayout.ShaderReadOnlyOptimal);
        imageInfos[1] = new DescriptorImageInfo(
            _sampler, _texture1View, ImageLayout.ShaderReadOnlyOptimal);
        var writes = stackalloc WriteDescriptorSet[3];
        for (var drawIndex = 0; drawIndex < _drawCapacity; drawIndex++)
        {
            var bufferInfos = new DescriptorBufferInfo[_bufferCount];
            for (var bufferIndex = 0; bufferIndex < _bufferCount; bufferIndex++)
            {
                bufferInfos[bufferIndex] = new DescriptorBufferInfo(
                    _guestBuffers[drawIndex][bufferIndex],
                    0,
                    _bufferSizes[bufferIndex]);
            }
            fixed (DescriptorBufferInfo* pBufferInfos = bufferInfos)
            {
                writes[0] = new WriteDescriptorSet
                {
                    SType = StructureType.WriteDescriptorSet,
                    DstSet = _descriptorSets[drawIndex],
                    DstBinding = 0,
                    DescriptorCount = (uint)_bufferCount,
                    DescriptorType = DescriptorType.StorageBuffer,
                    PBufferInfo = pBufferInfos,
                };
                for (uint imageIndex = 0; imageIndex < 2; imageIndex++)
                {
                    writes[imageIndex + 1] = new WriteDescriptorSet
                    {
                        SType = StructureType.WriteDescriptorSet,
                        DstSet = _descriptorSets[drawIndex],
                        DstBinding = imageIndex + 1,
                        DescriptorCount = 1,
                        DescriptorType = DescriptorType.CombinedImageSampler,
                        PImageInfo = &imageInfos[imageIndex],
                    };
                }
                _vk.UpdateDescriptorSets(_device, 3, writes, 0, null);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_device.Handle != 0)
        {
            _vk.DeviceWaitIdle(_device);
            if (_readbackBuffer.Handle != 0) _vk.DestroyBuffer(_device, _readbackBuffer, null);
            if (_readbackMemory.Handle != 0) _vk.FreeMemory(_device, _readbackMemory, null);
            if (_hostVertexBuffer.Handle != 0) _vk.DestroyBuffer(_device, _hostVertexBuffer, null);
            if (_hostVertexMemory.Handle != 0) _vk.FreeMemory(_device, _hostVertexMemory, null);
            if (_descriptorPool.Handle != 0) _vk.DestroyDescriptorPool(_device, _descriptorPool, null);
            if (_pipeline.Handle != 0) _vk.DestroyPipeline(_device, _pipeline, null);
            if (_geometryModule.Handle != 0) _vk.DestroyShaderModule(_device, _geometryModule, null);
            if (_fragmentModule.Handle != 0) _vk.DestroyShaderModule(_device, _fragmentModule, null);
            if (_vertexModule.Handle != 0) _vk.DestroyShaderModule(_device, _vertexModule, null);
            if (_pipelineLayout.Handle != 0) _vk.DestroyPipelineLayout(_device, _pipelineLayout, null);
            if (_setLayout.Handle != 0) _vk.DestroyDescriptorSetLayout(_device, _setLayout, null);
            if (_framebuffer.Handle != 0) _vk.DestroyFramebuffer(_device, _framebuffer, null);
            if (_renderPass.Handle != 0) _vk.DestroyRenderPass(_device, _renderPass, null);
            if (_targetView.Handle != 0) _vk.DestroyImageView(_device, _targetView, null);
            if (_targetImage.Handle != 0) _vk.DestroyImage(_device, _targetImage, null);
            if (_targetMemory.Handle != 0) _vk.FreeMemory(_device, _targetMemory, null);
            if (_sampler.Handle != 0) _vk.DestroySampler(_device, _sampler, null);
            if (_texture1View.Handle != 0) _vk.DestroyImageView(_device, _texture1View, null);
            if (_texture1Image.Handle != 0) _vk.DestroyImage(_device, _texture1Image, null);
            if (_texture1Memory.Handle != 0) _vk.FreeMemory(_device, _texture1Memory, null);
            if (_texture0View.Handle != 0) _vk.DestroyImageView(_device, _texture0View, null);
            if (_texture0Image.Handle != 0) _vk.DestroyImage(_device, _texture0Image, null);
            if (_texture0Memory.Handle != 0) _vk.FreeMemory(_device, _texture0Memory, null);
            for (var drawIndex = 0; drawIndex < _guestBuffers.Length; drawIndex++)
            {
                if (_guestBuffers[drawIndex] is null || _guestMemories[drawIndex] is null)
                {
                    continue;
                }
                for (var bufferIndex = 0; bufferIndex < _guestBuffers[drawIndex].Length; bufferIndex++)
                {
                    if (_guestBuffers[drawIndex][bufferIndex].Handle != 0)
                    {
                        _vk.DestroyBuffer(_device, _guestBuffers[drawIndex][bufferIndex], null);
                    }
                    if (_guestMemories[drawIndex][bufferIndex].Handle != 0)
                    {
                        _vk.FreeMemory(_device, _guestMemories[drawIndex][bufferIndex], null);
                    }
                }
            }
            if (_commandPool.Handle != 0) _vk.DestroyCommandPool(_device, _commandPool, null);
            _vk.DestroyDevice(_device, null);
        }
        if (_instance.Handle != 0)
        {
            _vk.DestroyInstance(_instance, null);
        }
        _vk.Dispose();
    }
}
