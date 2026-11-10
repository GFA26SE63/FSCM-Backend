# Local review2 snapshot — Backend

This branch preserves the saved local edits from `fix/review2-db-flow-alignment` at `5231622c5df555512ca116715a69336463bc8acd` plus the local source/demo assets listed in `MANIFEST.json`.

It is an archival checkpoint, not a tested integration into current `develop`. Many edits already exist on develop; other files are older and omit newer protections or routes. Compare and port individual changes before merging. No application code is executed by this archival operation.

The original working tree, index, branches and stash are preserved. Credentials, runtime secret configuration, logs and PostgreSQL storage are excluded. Demo scripts are historical and require their own local credentials and a disposable test environment. Some captured scripts reference paths from the original local setup and need adaptation before use elsewhere.
