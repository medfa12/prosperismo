# Host: Azure NGads V620 (RDNA2)

## Why

The PS5's GPU is RDNA2. Development ran on an NVIDIA T4 (Turing), which means the graphics path is
translated against an architecture that lacks the features the guest is using. The two warnings that
fire on Superliminal's own draws are both of that kind:

```
[SPIRV][WARN] ngg-prim-export-dropped ... NGG primitive connectivity cannot be expressed in the
              vertex stage; the draw's index buffer is used instead
[LOADER][WARN] gpu.unmapped_surface_htile ... no HTILE decoder exists; depth is consumed as if raw
```

NGG primitive shaders and HTILE depth compression are RDNA2 features. On a Radeon PRO V620 they have
native equivalents, so this host exists to stop guessing at them.

It is not a fix for anything CPU-side. Superliminal's remaining blocker is that its load never
finishes, which is not an architecture problem.

## The machine

| | |
|---|---|
| Instance | `sharpemu-v620`, resource group `sharpemu-rg` |
| Region | `eastus2` (the only region where quota was granted) |
| Size | `Standard_NG16ads_V620_v1` - 16 vCPU, half a Radeon PRO V620 |
| OS | Windows 11 Pro 24H2, build 26100 |
| Licence | `licenseType: Windows_Client` - Azure Hybrid Benefit / BYOL |
| Disk | 512 GB Premium SSD |
| GPU driver | `Microsoft.HpcCompute / AmdGpuDriverWindows` extension |

Quota note: `standardNGADSV620v1Family` starts at **0** on a new subscription and the SKU is
offered in `eastus2`, `centralus`, `westus3`, `uksouth`, `westeurope`, `swedencentral` and
`japaneast`. A request for 32 vCPU (a full V620) was granted at 16.

## Remote execution: use Run Command, not SSH

```bash
az vm run-command invoke -g sharpemu-rg -n sharpemu-v620 \
  --command-id RunPowerShellScript \
  --scripts '<powershell>' \
  --query "value[0].message" -o tsv
```

This goes through the Azure guest agent. No open port, no sshd, nothing to break.

That matters because the previous host was reached over SSH and it cost a lot of time: the OpenSSH
**server** payload disappeared from `C:\Windows\System32\OpenSSH` (client tools remained), so the
capability reported `NotPresent`, `Add-WindowsCapability` silently no-op'd against a damaged
component store, no `sshd` service existed to start, and the Google guest agent logged
`Could not determine if openssh version is compatible` on every boot. Repair needed
`DISM /Online /Cleanup-Image /RestoreHealth` before the capability would reinstall. Run Command has
none of that surface.

Interactive access is RDP on 3389 (the Windows App on macOS works fine). The NSG is created with an
RDP rule only.

## Layout

Deliberately not the previous host's layout, which accumulated ~150 scratch files in `C:\` root and
a checkout called `C:\r1`.

```
C:\sharpemu\              clean checkout of origin/master
C:\sharpemu\artifacts\    build output - produced here, never copied between machines
C:\sharpemu\games\        game dumps and firmware (gitignored)
C:\sharpemu\inspiration\  reference emulators (gitignored, re-cloned rather than copied)
C:\tools\                 git (MinGit), azcopy, dotnet-install
C:\dotnet\                .NET SDK 10.0.302
```

`games/` and `inspiration/` sit inside the checkout, matching the layout the code and scripts
expect. `inspiration/` is seven public clones, so it is fetched on the host rather than shipped.
`artifacts/` is deliberately never copied - it is build output.

Deliberately not the previous host's layout, which accumulated ~150 scratch files in `C:\` root and
a checkout called `C:\r1`.

## First run on RDNA2

Vulkan selects `AMD Radeon Pro V620 MxGPU (DiscreteGpu)`. One warning class disappears immediately:

```
gpu.unmapped_surface_htile   T4: fires      V620: 0
```

HTILE is an RDNA2 depth format, and on RDNA2 there is nothing to emulate. `ngg-prim-export-dropped`
still fires once, so the NGG amplification gap is not purely an architecture artefact.

The title does **not** yet get as far here as it did on the T4: one flip and no presents, against 343
flips and a rendered title screen on Turing, with the same environment. The process survives (no
CET fail-fast, no access violations, and the TRC watchdog's "not safe" path never trips). That is a
host-specific blocker and needs its own baseline before the two are compared - changing GPU vendor
changes the variable, and none of the T4 measurements carry over automatically.

## Transferring a dump onto this host

The previous host had no working shell, so the dump was relayed through blob storage rather than
copied directly:

1. create a storage account + container, mint a container SAS with `racwdl`
2. on the source host, drive `azcopy` from a `windows-startup-script-ps1` and report progress to
   `COM1` so the serial console shows it (`get-serial-port-output`)
3. on this host, `azcopy copy` the container prefix down

14,956,857,416 bytes moved this way, verified as 162 files on arrival. Note that a `tar` built on
macOS carries `.DS_Store` and `._*` AppleDouble sidecars, which have to be deleted after extraction
or they show up in guest file enumeration.
