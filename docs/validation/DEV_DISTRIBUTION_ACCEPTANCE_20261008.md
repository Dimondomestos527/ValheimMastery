# Dev distribution acceptance — 2026-10-08

Published source, documentation, QC/tools, approved milestone art and run-on-demand updaters to Dev. Source import commit:8f916b65f2bb7cf11196b1461b4853f1746c495c. Main unchanged. Current editable Dev source may contain pending implementation;ready distribution packages contain only reviewed installed1.4.123-20261008 binaries and have a separate matching baseline source folder.

PASS11updater tests:complete commit-pinned download flow,Client/Server package installation,owned stale asset removal/unmanaged-file preservation,corrupt hashes,unsafe paths,variant mismatch,lock/running-game refusal,rollback after partial failure,case-only rename protection. Actual public GitHub updater ZIP/source hashes checked;Windows PowerShell5.1 wrapper downloaded Client and Server and installed in separate disposable folders;both final DLL hashes matched manifests. No live game/client/server target edited or launched by these tests.

Publication audit verified1370initialtracked files,59PNG images byte-identical to approved installed runtime resources,2ZIPs containing only exactreviewedDLL,updaterZIP containing only exactauthoredscripts/UAinstructions. No raw Valheim/Unity DLLs,configs,saves,journal state,private runtime logs or build caches included. Historical docs can reference excluded local validation evidence;those links are not hosted evidence.

LIMIT:ActualmacOS execution/game runtime UNTESTED. Python3.9+andcompatibleexistingBepInEx5 required. Checksums validate consistency with the trusted repository,not an independent signature. Scripts do not self-update or continuously watch. Rollback/usage/future package publishing:updater/README_UA.md and README_UA.md. Gameplay migration baseline remains INCOMPLETE.
