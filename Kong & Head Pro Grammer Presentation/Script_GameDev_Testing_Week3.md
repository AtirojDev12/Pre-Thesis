# สคริปต์พูด — GameDev & Testing (Week 3)
**13RoH · ผู้พูด: นาย ก้องกิดากร ช่วยแจ้ง (Head Programmer)**

ครอบคลุมสไลด์ **6 (GameDev)**, **7 (Testing – MainMenu)**, **8 (Testing – TestWalk)**
เวลารวม: **4 นาที** — สไลด์ 6 ใช้ 2:00 / สไลด์ 7 ใช้ 0:50 / สไลด์ 8 ใช้ 1:10

บรรทัดที่มี ▸ คือคำสั่งการแสดง ไม่ต้องอ่านออกเสียง

> **กฎข้อเดียวของสคริปต์นี้: ห้ามอ่านทุก bullet บนสไลด์**
> มี 16 bullet ใน 2 นาทีเป็นไปไม่ได้ครับ พูดหัวข้อคอลัมน์ + จุดเด่น 2 อย่างต่อคอลัมน์ ที่เหลือให้คนอ่านเอง

---

## สไลด์ 6 — GameDev (2:00)

ส่วนของ GameDev ครับ สัปดาห์นี้มีเรื่องเดียวที่อยากให้ทุกคนจำกลับไป คือ **ตอนนี้เกมเล่นออนไลน์ข้ามเครื่องได้จริงแล้วครับ**

▸ *หยุด 1 วินาที ให้ประโยคนี้ลงก่อน แล้วชี้คอลัมน์ซ้าย*

คอลัมน์แรก **ระบบพื้นฐาน** — ระบบเซฟข้อมูลในเครื่องเข้ารหัสด้วย AES-256 เปิดด้วย Notepad แล้วแก้ค่าเงินไม่ได้ ตัวระบบโหลดอัตโนมัติก่อนเข้า Scene แรก และไอเทมถาวรจำกัดไว้ 1 ชิ้น หายเมื่อทิ้งหรือเสียชีวิต

▸ *ชี้คอลัมน์กลาง — ตรงนี้คือเนื้อหาหลักของสัปดาห์ ใช้เวลาเยอะสุด*

คอลัมน์กลาง **ระบบเครือข่าย** เป็นงานหลักของสัปดาห์นี้ครับ

เราผสาน Mirror กับ Epic Online Services ได้สำเร็จ และ Build ผ่านโดยไม่มี Error
กดสร้างห้อง ระบบจะสร้าง EOS Lobby แล้วตั้งตัวเองเป็น Host
กดค้นหาห้อง จะเห็นห้องที่เปิดอยู่ทั้งหมด พร้อมแผนที่ ระดับความยาก และจำนวนผู้เล่น
และการเข้าห้องใช้ Epic Product User ID ครับ **ไม่ต้องพิมพ์ IP ไม่ต้องทำ Port Forwarding** — สำหรับเกมปาร์ตี้เรื่องนี้สำคัญ ถ้าชวนเพื่อนเล่นยาก เกมก็จบแค่นั้นครับ

▸ *ชี้บรรทัด "ทดสอบการเล่นระหว่างคอมพิวเตอร์ 2 เครื่องสำเร็จ" — นี่คือบรรทัดสำคัญที่สุดของสไลด์*

บรรทัดนี้ครับ ทดสอบข้ามคอมพิวเตอร์ 2 เครื่องสำเร็จ ผู้เล่นทั้งสอง Spawn ได้ เคลื่อนที่ได้ และเห็นกันจริง

▸ *ชี้คอลัมน์ขวา*

คอลัมน์ขวา **ระบบการเล่น** — การเคลื่อนไหวและกล้องมุมมองบุคคลที่หนึ่ง ระบบปฏิสัมพันธ์กับประตูและคันโยก ระบบเลือดและความเสียหาย และระบบ Downed กับ Revive ที่มีตัวจับเวลาซิงก์กันระหว่างผู้เล่น

▸ *ชี้ bullet สุดท้าย แล้วพูดช้าลง*

บรรทัดสุดท้ายสำคัญครับ — ทั้งหมดนี้ถูกเขียนใหม่ให้ทำงานภายใต้ **Server Authority** หมายความว่าเซิร์ฟเวอร์เป็นคนตัดสินทุกอย่าง ฝั่งผู้เล่นทำได้แค่ "ขอ" — ขอเปิดประตู ขอใช้ไอเทม — แต่ไม่มีสิทธิ์ประกาศว่ามันเกิดขึ้นแล้ว

ในเกมของเราเรื่องนี้จำเป็นครับ เพราะแรงกดดันจะไม่ทำงานเลย ถ้ากฎที่แต่ละคนเจอไม่เหมือนกัน

---

## สไลด์ 7 — Testing: MainMenu (0:50)

▸ *บอกก่อนเลยว่าเป็นภาพจริง ไม่ใช่ Mockup*

นี่คือฉากเมนูที่เราใช้ทดสอบจริงครับ เป็นภาพจากการเล่นจริง ไม่ใช่ภาพ Mockup

มีสองปุ่ม CreateRoom กับ FindRoom
CreateRoom เปิด Lobby บน Epic แล้วเริ่มเป็น Host
FindRoom ค้นหาและคืนค่าห้องที่เปิดอยู่ทั้งหมด

ค่าของห้อง — แผนที่ ระดับความยาก จำนวนผู้เล่น — ถูกส่งไปเก็บเป็น Attribute บน Lobby ของ Epic ทำให้หน้ารายการห้องแสดงข้อมูลได้ **ก่อน** ที่ผู้เล่นจะกดเข้า ผู้เล่นจะได้เลือกห้อง ไม่ใช่เดาสุ่มแล้วมารู้ทีหลัง

▸ *ถ้าเวลาเหลือให้พูดย่อหน้านี้ ถ้าไม่เหลือให้ตัดได้เลย*

มีจุดหนึ่งที่อยากเล่าครับ รหัสห้องเราตั้งใจ **ไม่** เก็บเป็น Attribute เพราะอ่านเอกสารของ Epic แล้วพบว่า Attribute ที่ตั้งเป็น Private คนที่ค้นหา Lobby ก็ยังอ่านได้อยู่ ถ้าเก็บรหัสไว้ตรงนั้นเท่ากับเปิดเผยรหัส เราจึงตรวจรหัสหลังเข้าห้องแทน

---

## สไลด์ 8 — Testing: TestWalk (1:10)

สไลด์นี้เป็นฉากที่เราใช้ทดสอบระบบการเล่นครับ

▸ *ชี้ภาพบน*

ภาพบนคือ UI หลอดเลือดและแผง Downed ที่ต่อกับตัวผู้เล่นเรียบร้อยแล้ว

▸ *ชี้ Console ในภาพล่าง — ตรงนี้คือหลักฐาน*

ภาพล่างขอให้ดูที่ Console ครับ
บรรทัดแรกบอกว่ายังไม่มีไฟล์เซฟ เริ่มใหม่ แล้วโหลดเซฟสำเร็จ Currency เท่ากับ 0 — นั่นคือระบบเซฟทำงานจริง
ถัดลงมาเป็นดาเมจ เลือดลดจาก 90 เป็น 80 เป็น 70 — นั่นคือระบบเลือดทำงานจริง

▸ *ย่อหน้านี้คือส่วนที่น่าจำที่สุดของสไลด์ พูดให้ช้าและตรงไปตรงมา*

แล้วมีปัญหาหนึ่งที่อยากเล่า เพราะใช้เวลานานที่สุดในสัปดาห์นี้ครับ

Mirror จะปิดวัตถุในฉากที่มี NetworkIdentity ทุกชิ้นโดยอัตโนมัติ เพื่อให้เซิร์ฟเวอร์เป็นคนสั่งว่าจะให้ปรากฏเมื่อไหร่ — ในเกมจริงถูกต้องครับ
แต่ในฉากทดสอบไม่มีเซิร์ฟเวอร์ พอกด Play วัตถุที่ Interact ได้ก็หายไปหมด ทำให้เพื่อนในทีมทดสอบงานตัวเองไม่ได้เลยถ้าไม่เปิดห้องก่อน

เราจึงทำโหมดออฟไลน์แยกออกมา ถ้าไม่มีทั้งเซิร์ฟเวอร์และไคลเอนต์ ระบบจะวางผู้เล่นทดสอบให้ และเปิดวัตถุที่ Mirror ปิดไว้กลับมา

ตรงนี้แก้สามรอบครับ สองรอบแรกพลาดเพราะเข้าใจผิดว่า Mirror ปิดวัตถุ **ก่อน** Awake แต่จริง ๆ ปิด **หลัง** Awake ทำให้โค้ดเราทำงานเร็วเกินไปและไม่เจออะไรเลย จนไปอ่าน Source Code ของ Mirror เองถึงเจอ

จบส่วนของ GameDev ครับ

---

# ถ้าเวลาไม่พอ — ตัดตามลำดับนี้

1. ย่อหน้าเรื่องรหัสห้อง (สไลด์ 7) — ประหยัด 20 วินาที
2. คอลัมน์ซ้าย "ระบบพื้นฐาน" ย่อเหลือประโยคเดียว: *"ระบบเซฟเข้ารหัส AES-256 ทำเสร็จแล้วครับ"* — ประหยัด 20 วินาที
3. เรื่อง "แก้สามรอบ" (สไลด์ 8) ย่อเหลือ: *"ตรงนี้แก้สามรอบครับ เพราะเข้าใจลำดับการทำงานของ Mirror ผิด"* — ประหยัด 20 วินาที

**ห้ามตัด:** บรรทัด "ทดสอบ 2 เครื่องสำเร็จ" และย่อหน้า Server Authority สองอันนี้คือสาระของทั้งส่วน

---
---

# English version

## SLIDE 6 — GameDev (2:00)

This is the GameDev section. There's one thing I want you to take away from this week: **the game is now genuinely playable online between two machines.**

▸ *Pause. Then point at the left column.*

First column, **Foundations** — an AES-256 encrypted local save system. You can't open it in Notepad and change your currency. It loads automatically before the first scene, and permanent items are capped at one copy and lost on drop or death.

▸ *Point at the middle column — this is the bulk of the week.*

The middle column, **Networking**, is the main work this week.

We integrated Mirror with Epic Online Services and the build passes with no errors.
Press Create Room and it opens an EOS Lobby, then makes you the host.
Press Find Room and you see every open room, with its map, difficulty and player count.
Joining uses the Epic Product User ID — **no IP address, no port forwarding.** For a party game that matters: if inviting your friends is hard, the game ends right there.

▸ *Point at the "2-machine test succeeded" line. This is the most important line on the slide.*

This line here. A two-machine test succeeded — both players spawn, both move, and they can see each other.

▸ *Point at the right column.*

Right column, **Gameplay** — first-person movement and camera, interaction with doors and levers, health and damage, and a downed-and-revive system with a timer synced between players.

▸ *Point at the last bullet and slow down.*

The last line matters. All of this was rewritten to run under **Server Authority** — the server decides everything. A client can only *ask*: ask to open a door, ask to use an item. It never gets to declare that something happened.

For our game that's necessary, because the pressure doesn't work at all if the rules aren't the same for everyone.

## SLIDE 7 — Testing: MainMenu (0:50)

This is the menu scene we actually test in — a real screenshot, not a mockup.

Two buttons: CreateRoom and FindRoom. CreateRoom opens a lobby on Epic and starts hosting. FindRoom searches and returns every open room.

The room's values — map, difficulty, player count — are published as attributes on the Epic lobby, so the room list can show them **before** a player joins. They choose a room instead of gambling and finding out afterwards.

▸ *Cut this paragraph first if you're over time.*

One thing worth mentioning: we deliberately do **not** store the room password as an attribute. Reading Epic's documentation, an attribute marked private is still readable by anyone searching the lobby — so storing it there would make it public. We verify it after joining instead.

## SLIDE 8 — Testing: TestWalk (1:10)

This is the scene we use to test gameplay systems.

▸ *Point at the top image.*

The top image is the health bar UI and the downed panel, wired up to the player.

▸ *Point at the console in the bottom image — this is the evidence.*

For the bottom image, look at the console. The first lines say there's no save file yet, starting fresh, then save loaded with currency at zero — that's the save system working. Below that, health dropping from 90 to 80 to 70 — that's the damage system working.

▸ *This paragraph is the memorable part. Slow and direct.*

And one problem worth telling you about, because it took the longest this week.

Mirror automatically disables every scene object carrying a NetworkIdentity, so the server controls when each appears. In a real match that's correct. But a test scene has no server — so when you pressed Play, every interactable object vanished. My teammate couldn't test his own work unless I hosted a room first.

So we built a separate offline mode. With no server and no client running, it places a test player and switches back on the objects Mirror disabled.

This took three attempts. The first two failed because I assumed Mirror disables those objects **before** Awake — it actually happens **after**, so my code ran too early and found nothing. I only found that by reading Mirror's own source code.

That's the end of the GameDev section.

## If you run over — cut in this order

1. The room-password paragraph (slide 7) — saves 20 s
2. Compress the Foundations column to one sentence: *"The AES-256 encrypted save system is done."* — saves 20 s
3. Compress the three-attempts story to: *"This took three attempts, because I had Mirror's execution order wrong."* — saves 20 s

**Never cut:** the two-machine test line, and the Server Authority paragraph. Those two are the substance of the whole section.
