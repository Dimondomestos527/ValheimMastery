# UX_PERFORMANCE_PILOT_A_TOGGLE_20261007 progress
COMPLETE / 100 of 100 / LIVE UNTESTED.

- Contract/reservation 20: complete. Root shared flag contract accepted; frozen baseline hashes for `MasteryWindow.cs` and `MasteryPlugin.cs` match canonical at task start.
- Candidate implementation 35: complete. Native styled process-local toggle, real binding, duplicate/headless guards, dynamic scope copy, layout reservation and true focus restoration implemented in isolated snapshot.
- Client/server compile and focused static checks 25: complete. Both variants 0 errors; 34/34 source/IL checks pass; no owned-source warnings.
- Exact manifest, affected review and handoff 20: complete. Combined frozen snapshot preserves the exact UX hashes and root config proposal; Utility100 runtime consumes the same `ConfigEntry<bool>`, resets its weak cache/generation on SettingChanged and executes the baseline path while OFF. Combined client/server build is 0 errors, all ten root QC gates pass, and compiled preservation is PASS for both variants (`1305` foreign types unchanged, `24` exact accepted types).

Final UX hashes: `MasteryWindow.cs` `4AA0C88F...`; new toggle `CBEBA6F0...`; root-only config proposal `E188BD32...`. Combined DLL candidates: client `02A103FD67B6349C42638F2CA070B4405B9A76060E7FE12ECC03434161F273E0`, server `3913091057C5CF26F79156A532985CF4A5B29E3AD0839F03B5488691400006B0`. Earlier candidate hashes are obsolete. Source/static UX review is final; installation integrity and live FPS/UI behavior remain outside this record.

Post-install read-only audit: installed client/server hashes exactly match the approved candidates; rollback DLLs exactly match prior `E9D80254...` / `904EE306...`; all 10 protected config/journal samples are unchanged; one plugin DLL exists in each target; Valheim client/server process count is zero. Assets were excluded and neither runtime was launched. Live FPS/UI/native behavior remains untested.
