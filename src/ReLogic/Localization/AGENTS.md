# ReLogic.Localization.IME: text input

Despite the folder name this is only text input, not translations (those live in `Terraria/Localization/`).

## API

`IImeService`, reached through `Platform.Get<IImeService>()` (see [../OS/AGENTS.md](../OS/AGENTS.md)):

- `Enable()` / `Disable()` / `IsEnabled`: start and stop text input while a text box has focus.
- `AddKeyListener(Action<char>)` / `RemoveKeyListener`: receive typed characters.
- `CompositionString`, `IsCandidateListVisible`, `CandidateCount`, `GetCandidate(i)`, `SelectedCandidate`: the in-progress composition and candidate list for CJK input methods, drawn by `Terraria/GameContent/UI/Elements/UITextBox` and `Terraria/GameContent/UI/NPCChatPanel`.

## Implementations

- `FnaIme` (every OS; registered in `InitializeClientServices` by `LinuxPlatform`, `OsxPlatform` and `FnaWindowsPlatform`): wraps FNA's `TextInputEXT` (`StartTextInput`, `StopTextInput`, `TextInput` event).
  It only delivers **committed** characters: `CompositionString` is always empty and there is never a candidate list, so CJK input works only as far as the desktop's input method commits text directly.
- `UnsupportedPlatformIme`: the no-op placeholder registered before the window exists (and on servers).
- `WindowsIme` + `Windows/` (Imm32 P/Invoke): full composition and candidate support, but tied to XNA's WinForms window, so it's never used: Aether's `FnaWindowsPlatform` registers `FnaIme` on Windows too.

Improving CJK input on Linux would mean extending `FnaIme` with SDL's text-editing events, and drawing the composition in the existing UI code paths that already read `CompositionString`.
