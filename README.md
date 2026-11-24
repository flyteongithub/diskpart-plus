<h1 align="center">Diskpart+</h1>

<p align="center">
  A clean, modern GUI for Windows' built-in <code>diskpart</code>.<br>
  All the power of the command line, without guessing which disk is which.
</p>

<p align="center">
  <a href="https://github.com/flyteongithub/diskpart-plus/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/flyteongithub/diskpart-plus/actions/workflows/ci.yml/badge.svg"></a>
  <img alt="Windows 10 and 11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4">
  <a href="LICENSE"><img alt="MIT licence" src="https://img.shields.io/badge/licence-MIT-green"></a>
</p>

> **Status: early development.** The engine that plans, checks and writes diskpart scripts is done and tested. The Windows app is being built now, so there's nothing to download yet.

## Why

Diskpart+ puts a proper interface on top of diskpart.

## What it does

The plan for v1:

**See what you've got**
- Every disk, partition and volume with its size, file system, label and drive letter
- Real names for Mac (APFS, HFS+), Linux and Windows partition types instead of "Unknown"
- Drive type at a glance (NVMe, SSD, HDD, USB, virtual)

**Partitions and drive letters**
- Create, delete, format, extend and shrink partitions
- Assign, change and remove drive letters
- Delete protected partitions (EFI, recovery, OEM) that Disk Management greys out

**Whole disks**
- **Wipe and reuse** in one step: clear a Mac or Linux drive and get back one clean exFAT or NTFS partition
- `clean` and `clean all`
- Convert between GPT and MBR
- Clear a stuck read-only flag on "write protected" drives
- Bring disks online or take them offline
- Fix duplicate disk IDs, which often happen after cloning a drive
- Set partition type IDs and GPT attributes

**Virtual disks**
- Create, attach and detach VHD and VHDX files

**The app**
- Fluent design with light, dark and follow-system themes
- Live diskpart output plus a raw script view for anyone who wants to see exactly what ran

## How it keeps your data safe

- **You see the script first.** Every action becomes a plan that shows what will change, how risky it is and the exact diskpart commands. Nothing runs until you've looked at it.
- **It won't touch your Windows disk.** Wiping, converting or taking offline the disk Windows runs or boots from, the disk with the page file or the disk Diskpart+ is running from is blocked outright. There's no override for that.
- **You type to confirm.** Anything that erases data asks you to type something like `ERASE DISK 2` first.
- **It checks it's still the same drive.** Disk numbers can change when you plug things in or take them out. Right before running, Diskpart+ reads your disks again and refuses to carry on if disk 2 isn't the drive you picked.
- **It catches mistakes early.** FAT32 over 32 GB, labels that are too long, letters already in use, converting a disk that isn't empty. All of these get flagged before diskpart even starts.
- **One disk at a time.** A single plan can't erase data on more than one disk.

## What it can't do

Diskpart+ can tell you a partition is APFS or HFS+ and it can wipe it, but it can't open it or copy files off it. Windows has no driver for Mac file systems, so diskpart only ever sees them as raw partitions. If there's anything on the drive you need, back it up on a Mac first.

## Roadmap

- [x] **Engine:** disk models, partition type catalogue, safety rules, planner and script builder, all unit tested
- [ ] **Windows layer:** read disks through the Windows storage API and run diskpart with live output
- [ ] **App:** WPF interface with Fluent styling, plan preview and typed confirmation
- [ ] **First release:** portable exe and installer

## Requirements

- Windows 10 (22H2) or Windows 11, x64 or ARM64
- Administrator rights. diskpart needs them, so Diskpart+ asks once when it starts.

## Building from source

You'll need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```
git clone https://github.com/flyteongithub/diskpart-plus.git
cd diskpart-plus
dotnet test
```

The engine and its tests run on any OS. The app itself will need Windows.

## Project layout

```
src/
  DiskpartPlus.Core/         the engine: models, safety rules, planner, script builder
tests/
  DiskpartPlus.Core.Tests/   unit tests for the engine
docs/
  ARCHITECTURE.md            how the pieces fit together
```

`DiskpartPlus.Windows` (talks to Windows and diskpart) and `DiskpartPlus.App` (the WPF interface) will sit alongside Core.

## Contributing

Issues and pull requests are welcome. Have a read of [CONTRIBUTING.md](CONTRIBUTING.md) first, especially the part about testing on virtual disks instead of real ones.

## Licence

[MIT](LICENSE) © flyteongithub

Diskpart+ is an independent project. It isn't affiliated with or endorsed by Microsoft. It changes partitions on real disks, so keep backups.
