# Prosperismo environment flags — COMPLETE generated reference

_Auto-generated from `src/` string literals by `scripts/gen_env_flags.py`. **Regenerate after adding/removing a flag** — the previous hand-written list had drifted badly (14 documented flags no longer existed; 263 real flags were undocumented)._

Total flags found in code: **388**


## SharpEmu.CLI

- `SHARPEMU_DISABLE_MITIGATION_RELAUNCH` — SharpEmu.CLI/Program.cs
- `SHARPEMU_LOG_ALL_IMPORTS` — SharpEmu.CLI/FirmwareOracle.cs (+1 more)
- `SHARPEMU_MITIGATED_CHILD` — SharpEmu.CLI/Program.cs (+1 more)
- `SHARPEMU_RENDERDOC` — SharpEmu.CLI/Program.cs
- `SHARPEMU_RENDERDOC_CAPTURE` — SharpEmu.CLI/Program.cs
- `SHARPEMU_RENDERDOC_DLL` — SharpEmu.CLI/Program.cs

## SharpEmu.Core/Cpu

- `SHARPEMU_DISABLE_GUEST_ALLOCATOR_HOLE_RECOVERY` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_DISABLE_IMPORT_LOOP_GUARD` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs (+2 more)
- `SHARPEMU_DISABLE_LLE_LIBC` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_DISABLE_NATIVE_GUEST_WORKERS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.NativeWorker.cs
- `SHARPEMU_DISABLE_POSIX_SIGNALS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.PosixSignals.cs
- `SHARPEMU_DISABLE_RAW_HANDLER` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs (+1 more)
- `SHARPEMU_DUMP_CODE` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Diagnostics.cs
- `SHARPEMU_DUMP_FAULT_STACK_WINDOW` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_GUEST_ARGS` — SharpEmu.Core/Cpu/CpuDispatcher.cs
- `SHARPEMU_GUEST_THREADS_NATIVE_WORKER` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_HLE_MEMCPY` — SharpEmu.Core/Cpu/HleBindingOptions.cs
- `SHARPEMU_IGNORE_STACK_CHK` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs
- `SHARPEMU_IL2CPP_STUB_MISSING` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs
- `SHARPEMU_IMPORT_CENSUS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_IMPORT_CENSUS_INTERVAL` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.ImportCensus.cs
- `SHARPEMU_IMPORT_CENSUS_PATH` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.ImportCensus.cs
- `SHARPEMU_IMPORT_LOOP_GUARD_SECONDS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs
- `SHARPEMU_LLE_LIBC_ALL` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LLE_LIBC_SAFE_ONLY` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_BOOTSTRAP` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_CONTEXT` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_DISASM` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_LOG_DISASM_ADDRS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_LOG_DLSYM` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs
- `SHARPEMU_LOG_EXPECTED_IMPORT_RESULTS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs
- `SHARPEMU_LOG_FIBER` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs (+1 more)
- `SHARPEMU_LOG_GUEST_EXCEPTIONS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs (+1 more)
- `SHARPEMU_LOG_GUEST_THREADS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs (+1 more)
- `SHARPEMU_LOG_GUEST_THREAD_SNAPSHOTS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_HLE_HIST` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Diagnostics.cs
- `SHARPEMU_LOG_IL2CPP_EXCEPTION` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs
- `SHARPEMU_LOG_IL2CPP_LOOKUPS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs (+1 more)
- `SHARPEMU_LOG_IMPORT_FILTER` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_IMPORT_FRAMES` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_IMPORT_RECENT` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_IMPORT_STUB_MAP` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_LAZY_COMMIT` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_LOG_POINTER_WINDOWS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_LOG_POINTER_WINDOW_SIZE` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_LOG_POSIX_SIGNALS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.PosixSignals.cs
- `SHARPEMU_LOG_PS5_USER_SLOTS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Imports.cs
- `SHARPEMU_LOG_REFSCAN_ADDRS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_LOG_REGISTER_WINDOWS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Exceptions.cs
- `SHARPEMU_LOG_RIP_SAMPLE` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_SOUND_STATE` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_STACK_CHK` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_STRLEN` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_STRLEN_BURSTS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_THREAD_MODE` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_THREAD_STATE_MS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_LOG_USLEEP` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs (+1 more)
- `SHARPEMU_MAIN_ENTRY_NATIVE_WORKER` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_NATIVE_WORKER_MAX_CONCURRENT` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.NativeWorker.cs
- `SHARPEMU_PERF_HLE` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Diagnostics.cs
- `SHARPEMU_PERF_HLE_NODICT` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.Diagnostics.cs
- `SHARPEMU_PERF_MEM` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.PosixSignals.cs
- `SHARPEMU_PERIODIC_SNAPSHOT_SECONDS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_PROBE_IMPORT_RET` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_PROBE_IMPORT_RET_ADDRESS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_SENTINEL_PROBE` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_STALL_WATCHDOG_SECONDS` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs
- `SHARPEMU_TRACE_FOCUSED_CONTINUATION` — SharpEmu.Core/Cpu/Native/DirectExecutionBackend.cs

## SharpEmu.Core/Memory

- `SHARPEMU_LAZY_RESERVE_PRIME_MB` — SharpEmu.Core/Memory/PhysicalVirtualMemory.cs
- `SHARPEMU_LOG_VMEM` — SharpEmu.Core/Memory/PhysicalVirtualMemory.cs (+2 more)

## SharpEmu.Core/Runtime

- `SHARPEMU_APP0_DIR` — SharpEmu.Core/Runtime/SharpEmuRuntime.cs (+9 more)
- `SHARPEMU_LLE_MODULES` — SharpEmu.Core/Runtime/SharpEmuRuntime.cs
- `SHARPEMU_LOG_DATA_REBIND` — SharpEmu.Core/Runtime/SharpEmuRuntime.cs
- `SHARPEMU_MODULE_PRELOAD_FAILURE` — SharpEmu.Core/Runtime/SharpEmuRuntime.cs
- `SHARPEMU_PRELOAD_ALL_SCE_MODULES` — SharpEmu.Core/Runtime/SharpEmuRuntime.cs

## SharpEmu.GUI

- `SHARPEMU_BTHID_UNAVAILABLE` — SharpEmu.GUI/MainWindow.axaml.cs (+2 more)
- `SHARPEMU_DUMP_SPIRV` — SharpEmu.GUI/MainWindow.axaml.cs (+2 more)
- `SHARPEMU_LOG_DIRECT_MEMORY` — SharpEmu.GUI/MainWindow.axaml.cs (+2 more)
- `SHARPEMU_LOG_DISCORD` — SharpEmu.GUI/DiscordRichPresence.cs
- `SHARPEMU_LOG_IO` — SharpEmu.GUI/MainWindow.axaml.cs (+2 more)
- `SHARPEMU_LOG_NP` — SharpEmu.GUI/MainWindow.axaml.cs (+6 more)
- `SHARPEMU_RENDER_SCALE` — SharpEmu.GUI/MainWindow.axaml.cs (+1 more)
- `SHARPEMU_TRACE_SURFACE_SIZE` — SharpEmu.GUI/GameSurfaceHost.cs
- `SHARPEMU_VK_VALIDATION` — SharpEmu.GUI/MainWindow.axaml.cs (+2 more)
- `SHARPEMU_WRITABLE_APP0` — SharpEmu.GUI/MainWindow.axaml.cs (+2 more)

## SharpEmu.HLE

- `SHARPEMU_GUEST_IMAGE_CPU_SYNC` — SharpEmu.HLE/GuestImageWriteTracker.cs
- `SHARPEMU_LOG_SYNC` — SharpEmu.HLE/GuestSyncTrace.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_ADDRS` — SharpEmu.HLE/GuestImageWriteTracker.cs (+1 more)
- `SHARPEMU_TRACE_GUEST_MEMORY_LIFETIME` — SharpEmu.HLE/GuestImageWriteTracker.cs
- `SHARPEMU_WATCH_BULK_DEST_HI` — SharpEmu.HLE/GuestWriteWatch.cs
- `SHARPEMU_WATCH_BULK_TORN` — SharpEmu.HLE/GuestWriteWatch.cs
- `SHARPEMU_WATCH_POOL_HEADER` — SharpEmu.HLE/GuestWriteWatch.cs
- `SHARPEMU_WATCH_VALUE1` — SharpEmu.HLE/GuestWriteWatch.cs
- `SHARPEMU_WATCH_VALUE_PATTERN` — SharpEmu.HLE/GuestWriteWatch.cs

## SharpEmu.HLE/Diagnostics

- `SHARPEMU_FAST_BOOT` — SharpEmu.HLE/Diagnostics/EmulationCostProfile.cs
- `SHARPEMU_HDR` — SharpEmu.HLE/Diagnostics/EmulationCostProfile.cs
- `SHARPEMU_HLE_EFFECT_CENSUS` — SharpEmu.HLE/Diagnostics/HleEffectCensus.cs
- `SHARPEMU_HLE_EFFECT_CENSUS_INTERVAL` — SharpEmu.HLE/Diagnostics/HleEffectCensus.cs
- `SHARPEMU_HLE_EFFECT_CENSUS_REPORT` — SharpEmu.HLE/Diagnostics/HleEffectCensus.cs
- `SHARPEMU_HLE_EFFECT_CENSUS_TOP` — SharpEmu.HLE/Diagnostics/HleEffectCensus.cs
- `SHARPEMU_HLE_EFFECT_CENSUS_TRIGGER` — SharpEmu.HLE/Diagnostics/HleEffectCensus.cs
- `SHARPEMU_HLE_VERIFIED_NOOPS` — SharpEmu.HLE/Diagnostics/HleVerifiedNoOp.cs
- `SHARPEMU_LOG_EXPORT_CALLS` — SharpEmu.HLE/Diagnostics/ExportCallTrace.cs
- `SHARPEMU_LOG_EXPORT_CALLS_FLUSH_MS` — SharpEmu.HLE/Diagnostics/ExportCallTrace.cs
- `SHARPEMU_LOG_EXPORT_CALLS_LIB` — SharpEmu.HLE/Diagnostics/ExportCallTrace.cs
- `SHARPEMU_LOG_EXPORT_CALLS_PATH` — SharpEmu.HLE/Diagnostics/ExportCallTrace.cs
- `SHARPEMU_LOG_EXPORT_CALLS_RING` — SharpEmu.HLE/Diagnostics/ExportCallTrace.cs
- `SHARPEMU_MAX_HEIGHT` — SharpEmu.HLE/Diagnostics/EmulationCostProfile.cs
- `SHARPEMU_MAX_WIDTH` — SharpEmu.HLE/Diagnostics/EmulationCostProfile.cs
- `SHARPEMU_PROBE_SPEC` — SharpEmu.HLE/Diagnostics/GuestProbeEngine.cs
- `SHARPEMU_PROBE_SPEC_JSON` — SharpEmu.HLE/Diagnostics/GuestProbeEngine.cs
- `SHARPEMU_SAMPLE_STALLED_MS` — SharpEmu.HLE/Diagnostics/StalledThreadSampler.cs
- `SHARPEMU_SAMPLE_STALLED_THREADS` — SharpEmu.HLE/Diagnostics/StalledThreadSampler.cs
- `SHARPEMU_WATCH_WRITE` — SharpEmu.HLE/Diagnostics/GuestWriteWatchpoint.cs (+1 more)
- `SHARPEMU_WATCH_WRITE_DELAY_MS` — SharpEmu.HLE/Diagnostics/GuestWriteWatchpoint.cs

## SharpEmu.HLE/Host

- `SHARPEMU_ALSA_DEVICE` — SharpEmu.HLE/Host/Posix/PosixAlsaAudioStream.cs

## SharpEmu.Libs

- `SHARPEMU_LOG_GUARDS` — SharpEmu.Libs/CxxAbiExports.cs
- `SHARPEMU_LOG_STDIO` — SharpEmu.Libs/LibcStdioExports.cs
- `SHARPEMU_LOG_STDIO_CALLS` — SharpEmu.Libs/StdioCallTrace.cs
- `SHARPEMU_LOG_STDIO_CALLS_PATH` — SharpEmu.Libs/StdioCallTrace.cs

## SharpEmu.Libs/Agc

- `SHARPEMU_AGC_SUBMIT_COMPLETION_EVENT` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_BAKE_SGPRS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_DETILE` — SharpEmu.Libs/Agc/GnmTiling.cs
- `SHARPEMU_DISABLE_FILL_CLEAR` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_DUMP_SPIRV_ADDRESS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_GPU_DEADLOCK_BREAK_MS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_GPU_WAIT_FALLBACK_MS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_GPU_WAIT_MODE` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_LOG_AGC` — SharpEmu.Libs/Agc/AgcExports.cs (+1 more)
- `SHARPEMU_LOG_AGC_SHADER` — SharpEmu.Libs/Agc/AgcExports.cs (+1 more)
- `SHARPEMU_LOG_INDIRECT` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_LOG_SUBMIT_GATE` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_NO_TEXTURE_SKIP` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_QUIET_GPU_DEFAULTS` — SharpEmu.Libs/Agc/AgcTessellation.cs (+1 more)
- `SHARPEMU_STRICT_SHADER_DESCRIPTORS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TEXTURE_DUMP_DIR` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TEXTURE_LINEAR_DUMP_DIR` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_AGC_CONTEXT_STATE` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_COMPUTE_SHADER_ADDRESS` — SharpEmu.Libs/Agc/AgcExports.cs (+1 more)
- `SHARPEMU_TRACE_DRAWS` — SharpEmu.Libs/Agc/AgcExports.cs (+1 more)
- `SHARPEMU_TRACE_FRAME_PACKETS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_GPU_MEMORY_ADDRESS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_GUEST_IMAGES` — SharpEmu.Libs/Agc/AgcExports.cs (+1 more)
- `SHARPEMU_TRACE_NGG_INPUTS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_NGG_LAUNCH` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_PIXEL_SHADER_ADDRESS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_RENDER_TARGET_ADDRESS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_STORAGE_IMAGE_INIT_ADDRESS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_TITLE_GLOBALS` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_TITLE_GLOBALS_LIVE` — SharpEmu.Libs/Agc/AgcExports.cs
- `SHARPEMU_TRACE_VERTEX_RANGES` — SharpEmu.Libs/Agc/AgcExports.cs

## SharpEmu.Libs/Ampr

- `SHARPEMU_LOG_AMPR` — SharpEmu.Libs/Ampr/AmprExports.cs (+2 more)
- `SHARPEMU_LOG_AMPR_READS` — SharpEmu.Libs/Ampr/AmprExports.cs (+1 more)

## SharpEmu.Libs/AppContent

- `SHARPEMU_DOWNLOAD_DATA_DIR` — SharpEmu.Libs/AppContent/AppContentExports.cs
- `SHARPEMU_LOG_APP_CONTENT` — SharpEmu.Libs/AppContent/AppContentExports.cs
- `SHARPEMU_TEMP0_DIR` — SharpEmu.Libs/AppContent/AppContentExports.cs (+1 more)

## SharpEmu.Libs/Audio

- `SHARPEMU_AUDIOOUT2_WAV_PATH` — SharpEmu.Libs/Audio/AudioOut2Exports.cs
- `SHARPEMU_DISABLE_AUDIO_PROPAGATION` — SharpEmu.Libs/Audio/AudioPropagationExports.cs (+1 more)
- `SHARPEMU_DISABLE_SNDZ` — SharpEmu.Libs/Audio/AudioOut2Exports.cs (+1 more)
- `SHARPEMU_DUMP_GUEST_CODE` — SharpEmu.Libs/Audio/AudioOut2Exports.cs
- `SHARPEMU_LOG_AJM` — SharpEmu.Libs/Audio/AjmExports.cs
- `SHARPEMU_LOG_AUDIO` — SharpEmu.Libs/Audio/AudioOut2Exports.cs (+1 more)
- `SHARPEMU_LOG_AUDIO_OUT` — SharpEmu.Libs/Audio/AudioOutExports.cs
- `SHARPEMU_LOG_AUDIO_OUT2` — SharpEmu.Libs/Audio/AudioOut2Exports.cs
- `SHARPEMU_LOG_FMOD` — SharpEmu.Libs/Audio/FmodCompatExports.cs
- `SHARPEMU_LOG_SPEAKER_CALLER` — SharpEmu.Libs/Audio/AudioOut2Exports.cs

## SharpEmu.Libs/AvPlayer

- `SHARPEMU_AVPLAYER_PRESENT` — SharpEmu.Libs/AvPlayer/AvPlayerExports.cs (+1 more)
- `SHARPEMU_FFMPEG_PATH` — SharpEmu.Libs/AvPlayer/AvPlayerExports.cs

## SharpEmu.Libs/Bink

- `SHARPEMU_BINK_MODE` — SharpEmu.Libs/Bink/Bink2MovieBridge.cs

## SharpEmu.Libs/ContentExport

- `SHARPEMU_LOG_CONTENT_EXPORT` — SharpEmu.Libs/ContentExport/ContentExportExports.cs

## SharpEmu.Libs/DiscMap

- `SHARPEMU_LOG_DISCMAP` — SharpEmu.Libs/DiscMap/DiscMapExports.cs

## SharpEmu.Libs/Gpu

- `SHARPEMU_GPU_BACKEND` — SharpEmu.Libs/Gpu/GuestGpu.cs
- `SHARPEMU_METAL_AUTOKEY` — SharpEmu.Libs/Gpu/Metal/MetalHostInput.cs
- `SHARPEMU_METAL_DBG` — SharpEmu.Libs/Gpu/Metal/MetalVideoPresenter.cs
- `SHARPEMU_SKIP_ALL_COMPUTE` — SharpEmu.Libs/Gpu/Metal/MetalVideoPresenter.Compute.cs (+1 more)

## SharpEmu.Libs/Json

- `SHARPEMU_LOG_JSON` — SharpEmu.Libs/Json/JsonExports.cs

## SharpEmu.Libs/Kernel

- `SHARPEMU_DEVLOG_APP_DIR` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_DOWNLOAD0_DIR` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_FIRMWARE_DIR` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_FONT_SUBSTITUTE` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_GUEST_TIMEZONE` — SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs
- `SHARPEMU_HOSTAPP_DIR` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_IGNORE_GUEST_EXCEPTIONS` — SharpEmu.Libs/Kernel/KernelExceptionCompatExports.cs
- `SHARPEMU_LOG_ALLOC_IMPORTS` — SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs
- `SHARPEMU_LOG_CXA_ATEXIT` — SharpEmu.Libs/Kernel/KernelExports.cs
- `SHARPEMU_LOG_EQUEUE` — SharpEmu.Libs/Kernel/KernelEventQueueCompatExports.cs (+1 more)
- `SHARPEMU_LOG_EVENT_FLAG` — SharpEmu.Libs/Kernel/KernelEventFlagCompatExports.cs
- `SHARPEMU_LOG_IO_FILTER` — SharpEmu.Libs/Kernel/KernelFileTraceLog.cs
- `SHARPEMU_LOG_IO_QUIET` — SharpEmu.Libs/Kernel/KernelFileTraceLog.cs
- `SHARPEMU_LOG_LIBC_ALLOC` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_LOG_NET` — SharpEmu.Libs/Kernel/KernelSocketCompatExports.cs (+1 more)
- `SHARPEMU_LOG_OPEN` — SharpEmu.Libs/Kernel/KernelFileTraceLog.cs
- `SHARPEMU_LOG_PROC_PARAM` — SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs
- `SHARPEMU_LOG_PROC_PARAM_PTRS` — SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs
- `SHARPEMU_LOG_PTHREADS` — SharpEmu.Libs/Kernel/KernelExports.cs (+1 more)
- `SHARPEMU_LOG_PTHREAD_CONDS` — SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs
- `SHARPEMU_LOG_PTHREAD_MUTEX_FILTER` — SharpEmu.Libs/Kernel/KernelPthreadCompatExports.cs
- `SHARPEMU_LOG_PTHREAD_RWLOCK_FILTER` — SharpEmu.Libs/Kernel/KernelPthreadExtendedCompatExports.cs
- `SHARPEMU_LOG_SEMA` — SharpEmu.Libs/Kernel/KernelPosixSemExports.cs (+1 more)
- `SHARPEMU_LOG_SEMA_DEREF` — SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs
- `SHARPEMU_LOG_TIME_CONVERT` — SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs
- `SHARPEMU_LOG_UMTX` — SharpEmu.Libs/Kernel/KernelUmtxCompatExports.cs
- `SHARPEMU_LOG_UNWIND_MISSES` — SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs
- `SHARPEMU_LOG_VIRTUAL_MEMORY` — SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs
- `SHARPEMU_LOG_WIDE` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_LOG_WIDE_PRINTF` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_LOG_WIDE_PRINTF_ARGS` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_LOG_WIDE_PRINTF_FILTER` — SharpEmu.Libs/Kernel/KernelMemoryCompatExports.cs
- `SHARPEMU_NET_REDIRECT` — SharpEmu.Libs/Kernel/KernelSocketCompatExports.cs
- `SHARPEMU_SEMA_HOST_WAIT_ONLY` — SharpEmu.Libs/Kernel/KernelSemaphoreCompatExports.cs
- `SHARPEMU_STRICT_RWLOCK_WRITER_PREFERENCE` — SharpEmu.Libs/Kernel/KernelPthreadExtendedCompatExports.cs
- `SHARPEMU_TSC_FREQ_HZ` — SharpEmu.Libs/Kernel/KernelRuntimeCompatExports.cs

## SharpEmu.Libs/Network

- `SHARPEMU_LOG_HTTP` — SharpEmu.Libs/Network/HttpExports.cs
- `SHARPEMU_LOG_HTTP2` — SharpEmu.Libs/Network/Http2Exports.cs
- `SHARPEMU_LOG_SSL` — SharpEmu.Libs/Network/SslExports.cs

## SharpEmu.Libs/Ngs2

- `SHARPEMU_LOG_NGS2` — SharpEmu.Libs/Ngs2/Ngs2Exports.cs

## SharpEmu.Libs/Np

- `SHARPEMU_LOG_NP_WEB_API2` — SharpEmu.Libs/Np/NpWebApi2Exports.cs
- `SHARPEMU_NP_FAKE_SIGNED_IN` — SharpEmu.Libs/Np/NpManagerExports.cs
- `SHARPEMU_NP_FAKE_USERCTX` — SharpEmu.Libs/Np/NpWebApi2Exports.cs
- `SHARPEMU_SAVEDATA_DIR` — SharpEmu.Libs/Np/NpTrophy2Exports.cs (+1 more)

## SharpEmu.Libs/Pad

- `SHARPEMU_AUTO_CROSS` — SharpEmu.Libs/Pad/PadExports.cs
- `SHARPEMU_BTHID_CB_FAIL` — SharpEmu.Libs/Pad/BluetoothHidExports.cs
- `SHARPEMU_BTHID_EVENT_CODE` — SharpEmu.Libs/Pad/BluetoothHidExports.cs
- `SHARPEMU_BTHID_EVENT_SIZE` — SharpEmu.Libs/Pad/BluetoothHidExports.cs
- `SHARPEMU_BTHID_FIRE_CALLBACK` — SharpEmu.Libs/Pad/BluetoothHidExports.cs
- `SHARPEMU_LOG_PAD` — SharpEmu.Libs/Pad/PadExports.cs
- `SHARPEMU_PAD_AUTO_PRESS` — SharpEmu.Libs/Pad/PadExports.cs

## SharpEmu.Libs/PlayGo

- `SHARPEMU_LOG_PLAYGO` — SharpEmu.Libs/PlayGo/PlayGoExports.cs

## SharpEmu.Libs/Psml

- `SHARPEMU_LOG_PSML` — SharpEmu.Libs/Psml/PsmlExports.cs

## SharpEmu.Libs/Rtc

- `SHARPEMU_RTC_PROBE_RANGE` — SharpEmu.Libs/Rtc/RtcExports.cs

## SharpEmu.Libs/SaveData

- `SHARPEMU_LOG_SAVEDATA` — SharpEmu.Libs/SaveData/SaveDataDialogExports.cs (+1 more)

## SharpEmu.Libs/Share

- `SHARPEMU_LOG_SHARE` — SharpEmu.Libs/Share/ShareExports.cs

## SharpEmu.Libs/SystemService

- `SHARPEMU_PS5MANAGER_PROBE` — SharpEmu.Libs/SystemService/Ps5ManagerStateProbe.cs

## SharpEmu.Libs/UserService

- `SHARPEMU_LOG_USER_SERVICE` — SharpEmu.Libs/UserService/UserServiceExports.cs

## SharpEmu.Libs/VideoOut

- `SHARPEMU_ALLOW_FEEDBACK_LOOP` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_DUMP_FIXED_SOLID_FRAGMENT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_DUMP_TEXTURES` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_DUMP_VIDEOOUT` — SharpEmu.Libs/VideoOut/VideoOutExports.cs
- `SHARPEMU_ENABLE_CHUNKED_DRAWS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_ENABLE_WAYLAND` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FENCE_WAIT_TIMEOUT_MS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_ATTRIBUTE_FRAGMENT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_ATTRIBUTE_FRAGMENT_TARGETS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_DEFAULT_RASTER_STATE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_DEFAULT_RASTER_STATE_TARGETS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_EXPOSURE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_FULLSCREEN_PIPELINE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_FULLSCREEN_VERTEX` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_FULLSCREEN_VERTEX_TARGETS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_SOLID_FRAGMENT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_SOLID_FRAGMENT_TARGETS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_TITLE_DEFAULT_BLEND` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_TITLE_DEFAULT_RASTER_STATE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_TITLE_DEFAULT_VIEWPORT_SCISSOR` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_TITLE_DISABLE_CULL` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_TITLE_DISABLE_DEPTH` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_TITLE_FULLSCREEN_VERTEX` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_TITLE_SOLID_FRAGMENT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_TITLE_VERTEX_COLOR_WHITE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_WHITE_TEXTURE_BINDING_PCS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_WHITE_TEXTURE_SHADER_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FORCE_WHITE_TEXTURE_TARGETS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_FRAME_WAIT_BUDGET_MS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_GPU_TIMESTAMP_MIN_WORK_SEQUENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_GPU_TIMESTAMP_MRT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_GPU_TIMESTAMP_SHADER_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_GPU_TIMESTAMP_TEXTURES` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_GPU_TIMESTAMP_VERTICES` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_GUEST_IMAGE_DUMP_CONTINUOUS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_GUEST_IMAGE_DUMP_DIR` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_HOLD_FIRST_FLIP_MS` — SharpEmu.Libs/VideoOut/VideoOutExports.cs
- `SHARPEMU_HOLD_FLIP_NUMBER` — SharpEmu.Libs/VideoOut/VideoOutExports.cs
- `SHARPEMU_LOG_GPU_FENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_LOG_GPU_TIMESTAMP` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_LOG_PRESENT_RATE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_LOG_VIDEOOUT` — SharpEmu.Libs/VideoOut/VideoOutExports.cs
- `SHARPEMU_LOG_VIDEOOUT_FPS` — SharpEmu.Libs/VideoOut/VideoOutExports.cs
- `SHARPEMU_LOG_VK_RESOURCES` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_MAX_GUEST_WORK_PER_RENDER` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_NO_FLIP_PACING` — SharpEmu.Libs/VideoOut/VideoOutExports.cs
- `SHARPEMU_OVERLAY` — SharpEmu.Libs/VideoOut/PerfOverlay.cs
- `SHARPEMU_PENDING_GUEST_WORK_ITEMS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_PENDING_GUEST_WORK_MB` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_PRESENT_TEST_REPORT` — SharpEmu.Libs/VideoOut/VulkanPresenterSelfTest.cs
- `SHARPEMU_RENDERDOC_WAIT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_RENDER_WORK_BUDGET_MS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_SKIP_COMPUTE_CS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_SKIP_TALL_COMPUTE_Z` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_STRICT_TEXTURE_FORMAT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_SUBMISSION_CAPACITY_WAIT_MS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_SWAPCHAIN_DUMP_EVERY` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_COMPUTE_IMAGE_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_COMPUTE_IMAGE_AFTER_NONBLACK_PAIR` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_COMPUTE_IMAGE_CS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_COMPUTE_IMAGE_MIN_WORK_SEQUENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_COMPUTE_IMAGE_OCCURRENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_COMPUTE_TDR_BOUNDARY_CS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_DEPTH_INIT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GLOBAL_WRITEBACK_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_DRAW_COUNT_INTERVAL` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_DRAW_OCCURRENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_EVENTS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_FORMAT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_HEIGHT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_OCCURRENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_SHADER_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_IMAGE_WIDTH` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_SURFACE_ORDER_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_SURFACE_ORDER_MIN_WORK_SEQUENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_WORK_COMPLETION` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_WRITES` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_GUEST_WRITE_ORDINAL` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_NGG_REPLAY` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs (+1 more)
- `SHARPEMU_TRACE_NGG_REPLAY_SHADER_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_NONBLACK_PAIR_MAX_PROBES` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_NONBLACK_PAIR_REQUIRE_NGG_REPLAY` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_NONBLACK_PAIR_SHADER_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_NONBLACK_PAIR_SOURCE_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_NONBLACK_PAIR_START_OCCURRENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_NONBLACK_PAIR_TARGET_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_NONBLACK_PAIR_THRESHOLD` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PIXEL_SPIRV_BYTES` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PIXEL_SPIRV_OCCURRENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PREPOST_PAIR_MAX_PROBES` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PREPOST_PAIR_SHADER_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PREPOST_PAIR_SOURCE_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PREPOST_PAIR_START_OCCURRENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PREPOST_PAIR_TARGET_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PREPOST_PAIR_THRESHOLD` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PRESENTED_GUEST_IMAGE_ADDRS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_PRESENTED_GUEST_IMAGE_OCCURRENCE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_TITLE_DRAW` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_TRACE_TITLE_STATE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_VIEWPORT_EPSILON` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_VK_DEBUG_LABELS` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_VK_DEVICE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_VK_DEVICE_FAULT` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_VK_PIPELINE_CACHE` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs
- `SHARPEMU_VK_PIPELINE_CACHE_PATH` — SharpEmu.Libs/VideoOut/VulkanVideoPresenter.cs

## SharpEmu.Logging

- `SHARPEMU_LOG_FILE` — SharpEmu.Logging/SharpEmuLog.cs
- `SHARPEMU_LOG_LEVEL` — SharpEmu.Logging/SharpEmuLog.cs
- `SHARPEMU_LOG_NO_COLOR` — SharpEmu.Logging/SharpEmuLog.cs

## SharpEmu.ShaderCompiler

- `SHARPEMU_CFG_RESOURCE_DISCOVERY` — SharpEmu.ShaderCompiler/Gen5ShaderScalarEvaluator.cs
- `SHARPEMU_DUMP_SHADER_ADDR` — SharpEmu.ShaderCompiler/Gen5ShaderTranslator.cs
- `SHARPEMU_DUMP_SHADER_RESOURCES` — SharpEmu.ShaderCompiler/Gen5ShaderScalarEvaluator.cs
- `SHARPEMU_STRICT_BUFFER_LOAD` — SharpEmu.ShaderCompiler/Gen5ShaderScalarEvaluator.cs
- `SHARPEMU_STRICT_SCALAR_LOAD` — SharpEmu.ShaderCompiler/Gen5ShaderScalarEvaluator.cs
- `SHARPEMU_TRACE_VERTEX_RAW` — SharpEmu.ShaderCompiler/Gen5ShaderScalarEvaluator.cs

## SharpEmu.ShaderCompiler.Metal

- `SHARPEMU_SHADER_MAX_STEPS` — SharpEmu.ShaderCompiler.Metal/Gen5MslTranslator.cs (+1 more)

## SharpEmu.ShaderCompiler.Vulkan

- `SHARPEMU_CAPTURE_PIXEL_EXEC_PCS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_IMAGE_ADDRESS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_IMAGE_PC` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_IMAGE_VGPR_BASE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_SGPR_POINTS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_VGPR_ADDRESS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_VGPR_DEST_BASE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_VGPR_IGNORE_EXEC` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_VGPR_PC` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_VGPR_POINTS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_CAPTURE_PIXEL_VGPR_SOURCES` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_NGG_VISIBILITY` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.Alu.cs
- `SHARPEMU_FORCE_PACKED_EXPORT_ONE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_PACKED_EXPORT_STORE_ONE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_PACKED_STORE_EXEC_VALUES` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_PIXEL_EXPORT_ADDRESS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_PIXEL_EXPORT_EXEC_ADDRESS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_PIXEL_EXPORT_PACK_VGPR_BASE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_PIXEL_EXPORT_VGPR_ADDRESS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_PIXEL_EXPORT_VGPR_BASE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_PIXEL_MAGENTA` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_TITLE_COMPARE_4D4` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.Alu.cs
- `SHARPEMU_FORCE_TITLE_COMPARE_540` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.Alu.cs
- `SHARPEMU_FORCE_TITLE_EARLY_COLOR` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_TITLE_EXPORT_EXEC` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_TITLE_SINGLE_MRT` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_FORCE_TITLE_VERTEX_OUTPUTS_ONE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_MARK_PIXEL_PCS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_SHADER_CAP_PROBE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_SHADER_CAP_PROBE_SGPR` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_SHADER_STEP_PROBE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_SPIRV_GRAPHICS_WAVE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_SPIRV_STRICT_POSITION_EXPORTS` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_TRACE_PACKED_EXPORT` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_TRACE_TITLE_INTERFACE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs
- `SHARPEMU_TRACE_TITLE_SHADER_STATE` — SharpEmu.ShaderCompiler.Vulkan/Gen5SpirvTranslator.cs

## SharpEmu.Tests

- `SHARPEMU_FLIP_COMPOSITE_FIX` — SharpEmu.Tests/VulkanDeferSampledGuestTextureTests.cs
- `SHARPEMU_ORDERED_FLIP` — SharpEmu.Tests/VulkanOrderedFlipTests.cs (+1 more)
- `SHARPEMU_PRESENT_CONTENT_PREFER` — SharpEmu.Tests/VulkanPresenterFlipRedirectTests.cs
