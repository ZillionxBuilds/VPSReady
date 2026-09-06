# VPSReady middleman

Version-controlled handoff packets between the Owner/external reviewer and the engineer. These documents preserve scope, findings, reproduction cases and acceptance criteria across chats. They are not a second issue tracker, an agent runtime or a scheduler.

## Current packet

**[Pre-main R16-R18 follow-up](pre-main-r16-r18/00_START_HERE.md)**

Read the entry point before using [the execution prompt](pre-main-r16-r18/05_CODEX_EXECUTION_PROMPT.md).

- Intended instruction destination: `development`.
- Delivery branch while the documentation PR is pending: `docs/149-middleman-r16-r18`.
- Product repair base: fresh `origin/release/0.1.0`, NOT the older development product tree.
- Working model: one solo engineer, no subagents or simulated independent QA.
- Tracking: existing Issues and their canonical Workpads remain authoritative; use #149 / #1 / #20 and the existing Kanban.
- This packet is repair guidance, not permission to merge main or a statement that tests passed.

Development rejected the attempted documentation fast-forward with `Required status check "required" is expected.` The packet is therefore delivered through a documentation PR; no check/protection bypass was used. The files are usable from the delivery branch before that PR merges. Recheck actual PR state rather than assuming they already exist on development.

## Reading from another branch

Do not switch or reset a dirty working tree just to read these instructions. While the documentation PR is pending:

```sh
git fetch origin development release/0.1.0 docs/149-middleman-r16-r18
HANDOFF_SHA=$(git rev-parse origin/docs/149-middleman-r16-r18)
git show "$HANDOFF_SHA:middleman/pre-main-r16-r18/00_START_HERE.md"
git show "$HANDOFF_SHA:middleman/pre-main-r16-r18/05_CODEX_EXECUTION_PROMPT.md"
```

After the documentation PR is merged, `origin/development` may be used instead, provided it contains this folder. Record the chosen `HANDOFF_SHA` in the existing repair Workpad and read all packet files from that same SHA. This is the documentation SHA, not the product candidate or artifact SHA. Do not merge all of development into release merely to obtain this folder.

## ภาษาไทย

โฟลเดอร์นี้เก็บรายละเอียดสำหรับส่งงานข้าม Chat ให้ Codex อ่านเองได้ โดยไม่ต้องคัดลอก Prompt ยาวทุกครั้ง ปลายทางเอกสารคือ `development` แต่ระหว่างรอ required check ให้อ่านจาก branch `docs/149-middleman-r16-r18` ได้ทันที งานแก้ product ต้องต่อจาก `release/0.1.0` ล่าสุด และหยุดเพื่อให้ตรวจภายนอกก่อน merge เอกสารนี้ไม่ได้อนุมัติ `main` และไม่ได้แทนผลทดสอบจริง
