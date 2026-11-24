# Architecture

Diskpart+ is split so the risky part (deciding what to run) can be tested properly, away from Windows and away from the UI.

```
   UI picks what to do
          │
          ▼
   Operations             plain records like FormatPartition(disk 3, partition 1, NTFS)
          │
          ▼
   OperationPlanner       checks every step, works out the risk, builds the script
          │
          ▼
   OperationPlan          issues + risk + confirmation phrase + script + target fingerprints
          │
          │   destructive plans: one review screen; everything else runs straight away
          ▼
   Last-minute check      read the disks again, make sure disk N is still the same drive
          │
          ▼
   DiskpartRunner         diskpart /s, output streamed live
          │
          ▼
   Read the disks again   confirm the change actually happened
```

## Projects

| Project | What it does | Depends on |
|---|---|---|
| `DiskpartPlus.Core` | Models, partition types, operations, safety rules, planner, script builder | nothing |
| `DiskpartPlus.Windows` | Reads disks through the Windows storage API and runs diskpart | Core, System.Management |
| `DiskpartPlus.App` | The WPF interface | Core, Windows |

Core has no dependencies and doesn't call any Windows APIs. That's on purpose: every rule about what's safe to run can be unit tested on any machine, including CI.

## Reading disks

Disk state comes from the Windows storage management API (`MSFT_Disk`, `MSFT_Partition`, `MSFT_Volume` and `MSFT_PhysicalDisk` over CIM, via `CimStorageInventory`) and not from diskpart's own output. The page file comes from `Win32_PageFileUsage` and BitLocker status from `Win32_EncryptableVolume`.

diskpart's output is translated into whatever language Windows is set to, so anything that matches English words breaks for most of the world. It also rounds sizes and is slow to start. The storage API gives structured data with exact byte sizes in every language.

Diskpart+ only reads diskpart's output to show it to the user. It never uses it to decide what happened.

## Changing disks

All changes go through diskpart, so the tool stays transparent: what you see in the preview is exactly what runs.

The UI never writes diskpart text. It builds `DiskOperation` records and `DiskpartScriptBuilder` turns them into commands. That builder is the only place in the codebase with diskpart syntax in it.

A few rules the builder sticks to:

- **Every operation selects its own target.** Even if the step before already selected disk 2, the next step selects it again. It's a few extra lines, but no step can act on whatever happened to be selected last.
- **No `noerr`, ever.** diskpart stops at the first error, so nothing runs after something goes wrong.
- **Quoted values are checked.** Labels and file paths can't contain quotes or line breaks, so nothing typed into a text box can inject an extra command.
- **Every step gets a `rem` comment** so the raw script is readable.

## Planning

`OperationPlanner.Create` takes a list of operations and a snapshot of the disks. It walks the steps in order and keeps track of what earlier steps will have done by then. After a `clean`, for example, the old partitions are gone and the disk is RAW, so converting to GPT is fine but formatting "partition 2" isn't.

Each step goes through two checks:

- **`OperationValidator`** asks "will diskpart accept this?" FAT32 over 32 GB, labels too long for the file system, a drive letter already in use, converting a disk that still has partitions and so on.
- **`SafetyPolicy`** blocks anything destructive on critical disks. Apart from one warning about `clean all` on an SSD, it stays quiet. People using this know what diskpart does.

Every issue has a stable code (see `IssueCodes`) so tests and translations don't depend on message text.

### Risk levels

| Level | Examples | In the app |
|---|---|---|
| Safe | Assign a letter, attach a VHD | runs straight away |
| Caution | Shrink, take a disk offline, convert an empty disk | runs straight away |
| Destructive | Format, delete a partition, `clean` | one review screen, "Erase disk n" button |
| Critical | `clean all` | one review screen, "Wipe disk n" button |

### What counts as critical

A disk is critical if it holds the running copy of Windows, the system (EFI) partition the PC booted from, the page file or Diskpart+ itself. Destructive changes to a critical disk are blocked. Partitions on a critical disk that aren't critical themselves (a D: drive on the same SSD as C:, say) are fine.

### Same drive check

Disk numbers aren't stable. Unplug one USB drive and plug in another and "disk 2" can be a different drive. diskpart can only select disks by number, so every plan records a fingerprint of each disk it touches (number, size, unique ID, serial and name). Right before running, the app reads the disks again and `CheckTargetsStillMatch` refuses to go ahead if anything has changed.

## Running scripts

`DiskpartRunner` handles this:

- Each run writes its script to a fresh temp folder with a unique name and deletes it afterwards.
- `diskpart /s` runs with its output streamed line by line into the UI.
- Only one diskpart run at a time. Microsoft's docs say to leave about 15 seconds between back-to-back scripts so the previous one has fully shut down, so the runner waits if it needs to.
- Success is judged by the exit code and then by reading the disks again, never by parsing text.
- Most operations can't be cancelled once started. Killing diskpart halfway through a format or clean can leave a disk half written, so the UI only offers a stop button for `clean all`, which can take hours.

### Things we learned by testing on real Windows

These came out of the integration tests, which run every operation through real diskpart on throwaway VHDX files.

- **Exit codes:** Microsoft documents small exit codes (0 to 5) for scripts, but a failing command on current Windows returns a Windows error code instead, like `0x80070057`. Diskpart+ treats anything that isn't zero as a failure and shows the code in hex.
- **Script encoding:** diskpart reads script files in the console's OEM code page. ANSI, UTF-8 (with or without a BOM) and UTF-16 all come out garbled. So scripts are written in plain ASCII when possible and in the OEM code page otherwise. If a label or path has characters that code page can't hold, the planner blocks it with a clear message instead of letting diskpart write the wrong characters.
- **`convert gpt` adds an MSR:** on Windows, converting even a small disk to GPT creates a 16 MB Microsoft reserved partition first. Code that looks for "the data partition" shouldn't assume it's partition 1.
- **Extend can give back a bit more than shrink took:** diskpart may reclaim alignment space, so a partition can end up slightly bigger than before.

## Checking the UI

The app has a hidden `--render-to <folder>` switch. It opens the main window and a review window, saves PNGs of them in light and dark mode and quits without running diskpart. CI runs it on every push against a virtual disk laid out like a Mac drive, so a broken layout or a XAML error fails the build.

## Elevation

diskpart needs administrator rights, so the app's manifest asks for them up front (`requireAdministrator`). An unelevated UI talking to a separate elevated helper would be cleaner, but it doubles the plumbing and isn't worth it for v1.

One side effect: Windows blocks drag and drop from a normal Explorer window into an elevated app, so VHD paths are picked with a file dialog.
