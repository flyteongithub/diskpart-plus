# Contributing to Diskpart+

Thanks for wanting to help. This is a tool that can erase disks, so the bar for changes to the engine is a bit higher than usual. The rules below are there to keep people's data safe.

## Getting set up

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
2. Clone the repo and run `dotnet test`. Everything should pass before you change anything.

The engine (`DiskpartPlus.Core`) builds and tests on Windows, macOS and Linux. The app will need Windows.

## Ground rules

- **diskpart syntax only lives in `DiskpartScriptBuilder`.** If you need a new command, add an operation record and teach the builder about it. Don't build diskpart strings anywhere else.
- **Never add `noerr`.** A script should stop at the first error.
- **Never loosen the critical disk blocks.** No setting, flag or hidden option should let a destructive change through on the Windows disk, the boot disk, a page file disk or the disk the app runs from.
- **Every safety or validation rule needs a test.** Add it to `SafetyTests` or `ValidationTests`, ideally using one of the pretend disks in `TestDisks`.
- **New issues get a code in `IssueCodes`.** Tests should check the code, not the wording.
- **Write messages for normal people.** Short, plain English and say what to do next. "Disk 2 is offline. Bring it online first." is better than "Operation failed: VDS_E_DISK_OFFLINE".

## Testing on real hardware

Don't test anything destructive on a disk you care about. Make a throwaway VHDX instead:

```
diskpart
create vdisk file="C:\Temp\scratch.vhdx" maximum=2048 type=expandable
attach vdisk
```

It shows up as a normal disk you can partition, format and wipe as much as you like. Detach it and delete the file when you're done. `.vhd` and `.vhdx` files are in `.gitignore` so they won't get committed by accident.

## Pull requests

- Keep them focused. One fix or feature per PR is easiest to review.
- Say what changed and why. If it touches safety rules, say what you tested.
- Make sure `dotnet test` passes and the build has no warnings.

## Reporting bugs

Open an issue with your Windows version, what you did, what you expected and what happened. If diskpart output is involved, paste it in. Strip out anything you'd rather not share, like serial numbers.
