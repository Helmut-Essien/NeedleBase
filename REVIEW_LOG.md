# Password toggle slice — review log

## Iterations 1–4
See prior history: label/id fix, ARIA labels, single ImageButton, CSP-friendly JS, 44px targets, forced-colors, PrepareForDisplay remask paths, unit tests.

## Iteration 5
### Reviews
- Bugbot: clean · Security: clean · UI/UX: 4 findings · Senior: agree

### Fixed
- Remask before `IsBusy`; toggles later gated by `CanExecute=CanSubmit`
- Web remask on `visibilitychange` / `pageshow`
- Password field padding `14,2,4,2`
- `SemanticScreenReader` announce on toggle

## Iteration 6
### Fixed
- Web remask on form submit
- Mobile remask on `OnDisappearing` + `Window.Stopped`
- Toggle `CanExecute = CanSubmit` + `NotifyCanExecuteChanged`

## Iteration 7
### Reviews
- Bugbot: validator discarded 1 (ambiguous) · Security: Medium Register Shell reuse · UI/UX: `::-ms-reveal`

### Fixed
- `ClearAuthSensitiveStateMessage` on `ShowLoginAsync` clears Login + Register secrets
- Register clears on leave-to-login and successful register
- Hide Edge `::-ms-reveal` / `::-ms-clear`
- Login `PrepareForDisplay` remask-only (resume-safe); clear via logout message + successful login

## Iteration 8 (final)
### Reviews
- **Bugbot:** no bugs
- **Security:** no issues
- **Senior Developer:** clean (tests 8/8 pass; remask vs clear split correct)
- **UI/UX:** no issues

### Dismissed
| Finding | Evidence |
|---------|----------|
| Plaintext while user shows password | Intentional UX |
| App-wide CSP / frame headers | Pre-existing |
| Register non-password fields after logout | Same-device UX; passwords cleared on logout path |
| Register remask-only on Terms/Privacy return | Required to preserve in-progress passwords |

### Remaining open
- None. Loop complete.
