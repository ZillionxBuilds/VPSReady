# VPSReady middleman

Version-controlled handoff packets between the Owner/external reviewer and the engineer. These documents preserve scope, findings, reproduction cases and acceptance criteria across chats. They are not a second issue tracker, an agent runtime or a scheduler.

## Current packet

**[Pre-main R16-R18 follow-up](pre-main-r16-r18/00_START_HERE.md)**

Read the entry point before using [the execution prompt](pre-main-r16-r18/05_CODEX_EXECUTION_PROMPT.md).

- Instruction-storage branch: `development`.
- Product repair base: fresh `origin/release/0.1.0`, NOT the older development product tree.
- Working model: one solo engineer, no subagents or simulated independent QA.
- Tracking: existing Issues and their canonical Workpads remain authoritative; use #149 / #1 / #20 and the existing Kanban.
- Current packet is repair guidance, not permission to merge main or a statement that tests passed.

## Reading from another branch

Do not switch or reset a dirty working tree just to read these instructions.

```sh
git fetch origin development release/0.1.0
HANDOFF_SHA=$(git rev-parse origin/development)
git show "$HANDOFF_SHA:middleman/pre-main-r16-r18/00_START_HERE.md"
git show "$HANDOFF_SHA:middleman/pre-main-r16-r18/05_CODEX_EXECUTION_PROMPT.md"
```

Record `HANDOFF_SHA` in the existing repair Workpad and read the remaining packet files from that same SHA. This is the documentation SHA, not the product candidate or artifact SHA. Do not merge all of development into release merely to obtain this folder.

## ภาษาไทย

โฟลเดอร์นี้เก็บรายละเอียดสำหรับส่งงานข้าม Chat ให้ Codex อ่านเองได้ โดยไม่ต้องคัดลอก Prompt ยาวทุกครั้ง เอกสารอยู่ใน `development` แต่งานแก้ product ต้องต่อจาก `release/0.1.0` ล่าสุด และหยุดเพื่อให้ตรวจภายนอกก่อน merge เอกสารนี้ไม่ได้อนุมัติ `main` และไม่ได้แทนผลทดสอบจริง
