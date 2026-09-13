# สคริปต์พูด — GameDev & Testing (Week 3) · เวอร์ชัน 2 (สไลด์ 7–8 เป็นวิดีโอ)
**13RoH · ผู้พูด: นาย ก้องกิดากร ช่วยแจ้ง (Head Programmer)**

สไลด์ **6 (GameDev)**, **7 (Testing – ห้อง/ออนไลน์)**, **8 (Testing – TestWalk)**
เวลารวม **4:00** — สไลด์ 6 = 1:30 / สไลด์ 7 = 1:00 / สไลด์ 8 = 1:20 / เหลือกันชน 0:10

▸ = คำสั่งการแสดง · ▶ = คิววิดีโอ · 🔇 = ช่วงที่ต้องเงียบ

---

## ⚠ ต้องทำก่อนวันนำเสนอ — อ่านก่อน

วิดีโอสามไฟล์ของเรารวมกัน **172 วินาที** แต่ทั้งส่วนนี้มีเวลา **240 วินาที**
ถ้าเปิดเต็มทั้งสามไฟล์ จะไม่มีเวลาพูดเลยครับ

1. **ตัดวิดีโอให้เหลือไฟล์ละไม่เกิน 30–35 วินาที** เอาเฉพาะช่วงสำคัญ
   ถ้าตัดไม่ทัน ให้จดเวลาที่ต้องเลื่อนไปไว้ แล้วซ้อมเลื่อนให้คล่อง
2. **ปิดเสียงวิดีโอ** เสียง Unity Editor ไม่ได้ช่วยอะไร และเราจะพูดทับอยู่
3. **ทดสอบการเล่นบนเครื่องที่ใช้นำเสนอจริง** อย่างน้อยหนึ่งรอบ
4. **เตรียมไฟล์ mp4 ไว้ในโฟลเดอร์แยก** เผื่อ embed ไม่โหลด — ถ้าเน็ตห้องประชุมช้า สไลด์จะกลายเป็นกล่องเปล่า
5. **คลิกเล่นให้ไวและมั่นใจ** การคลำหาปุ่ม Play สิบวินาทีทำลายจังหวะทั้งส่วน

---

## สไลด์ 6 — GameDev (1:30)

▸ *อย่าอ่านทุก bullet — มี 16 ข้อ ถ้าอ่านหมดจะกินเวลาสามนาที*

ส่วนของ GameDev ครับ สัปดาห์นี้มีเรื่องเดียวที่อยากให้จำกลับไป คือ **ตอนนี้เกมเล่นออนไลน์ข้ามเครื่องได้จริงแล้วครับ** และเดี๋ยวผมจะเปิดให้ดูของจริง

▸ *ชี้คอลัมน์ซ้าย — พูดเร็ว ผ่านไป*

คอลัมน์ซ้าย **ระบบพื้นฐาน** — ระบบเซฟในเครื่องเข้ารหัส AES-256 เปิดด้วย Notepad แก้ค่าเงินไม่ได้ โหลดอัตโนมัติก่อนเข้า Scene แรก และไอเทมถาวรจำกัด 1 ชิ้น หายเมื่อทิ้งหรือตาย

▸ *ชี้คอลัมน์กลาง — ตรงนี้ช้าลง คือเนื้อหาหลัก*

คอลัมน์กลาง **ระบบเครือข่าย** คืองานหลักของสัปดาห์นี้ครับ
เราต่อ Mirror กับ Epic Online Services สำเร็จ และ Build ผ่านไม่มี Error
สร้างห้องได้ ค้นหาห้องได้ และเข้าห้องด้วย Epic Product User ID — **ไม่ต้องพิมพ์ IP ไม่ต้องทำ Port Forwarding**
สำหรับเกมปาร์ตี้เรื่องนี้สำคัญมากครับ ถ้าชวนเพื่อนเล่นยาก เกมก็จบแค่นั้น

▸ *ชี้คอลัมน์ขวา*

คอลัมน์ขวา **ระบบการเล่น** — เดิน กล้องบุคคลที่หนึ่ง ปฏิสัมพันธ์กับประตูกับคันโยก เลือดและความเสียหาย และระบบ Downed กับ Revive ที่จับเวลาซิงก์กันระหว่างผู้เล่น

▸ *ชี้ bullet สุดท้าย พูดช้าและชัด — นี่คือประโยคที่มีน้ำหนักที่สุดของสไลด์*

บรรทัดสุดท้ายสำคัญครับ ทั้งหมดนี้เขียนใหม่ให้ทำงานภายใต้ **Server Authority** — เซิร์ฟเวอร์เป็นคนตัดสินทุกอย่าง ฝั่งผู้เล่นทำได้แค่ "ขอ" แต่ไม่มีสิทธิ์ประกาศว่าเกิดขึ้นแล้ว
ในเกมของเราจำเป็นครับ เพราะกฎจะไม่สร้างแรงกดดันเลย ถ้ากฎของแต่ละคนไม่เหมือนกัน

ทีนี้ผมขอเปิดของจริงให้ดูครับ

---

## สไลด์ 7 — Testing: สร้างห้องและเข้าห้อง (1:00)

▸ *พูดสองประโยคนี้ก่อนกดเล่น เพื่อบอกกรรมการว่าต้องดูอะไร*

นี่คือคลิปจากการทดสอบจริงครับ **สองเครื่อง ไม่ใช่สองหน้าต่างบนเครื่องเดียว**
สิ่งที่ขอให้ดูคือ ผู้เล่นคนที่สองจะโผล่เข้ามาในฉากเดียวกัน

▶ **กดเล่น**

▸ *ช่วงต้นคลิป พูดสั้น ๆ ทับได้*

ตอนนี้กด Create Room ระบบกำลังเปิด Lobby บน Epic แล้วตั้งตัวเองเป็น Host
ค่าของห้อง — แผนที่ ระดับความยาก จำนวนผู้เล่น — ถูกส่งไปเก็บไว้กับ Lobby ด้วย
อีกเครื่องกดค้นหา แล้วเข้าห้องผ่าน Epic Product User ID

🔇 ▸ *ตอนที่ผู้เล่นคนที่สองปรากฏ — **หยุดพูด** ปล่อยให้ภาพพูดเอง 3–4 วินาที แล้วชี้จอ*

▸ *หลังจากนั้นพูดสั้น ๆ*

ตรงนี้ครับ สองตัวในฉากเดียวกัน เดินได้ เห็นกันจริง

▶ **หยุดวิดีโอ** (อย่าปล่อยให้เล่นต่อจนหมดคลิป)

▸ *ประโยคปิดสไลด์ ต้องพูด*

ผลนี้สัปดาห์ก่อนยังไม่สำเร็จครับ ไคลเอนต์เชื่อมต่อได้ แต่ตัวผู้เล่นไม่ขึ้นบนจออีกฝั่ง สาเหตุคือ Prefab ไม่ได้ลงทะเบียนตรงกันระหว่างสองเครื่อง — เซิร์ฟเวอร์สร้างจาก Prefab ที่ถืออยู่ได้เลย แต่ไคลเอนต์สร้างได้เฉพาะตัวที่ลงทะเบียนไว้

---

## สไลด์ 8 — Testing: TestWalk และโหมดออฟไลน์ (1:20)

▸ *บอกก่อนว่าคลิปนี้คนละเรื่องกับคลิปก่อน*

คลิปนี้เป็นฉากทดสอบระบบการเล่นครับ คนละเรื่องกับเมื่อกี้ — อันนี้คือ **โหมดออฟไลน์**

▶ **กดเล่น**

▸ *พูดทับช่วงต้น*

Mirror จะปิดวัตถุในฉากที่มี NetworkIdentity ทุกชิ้นอัตโนมัติ เพื่อให้เซิร์ฟเวอร์สั่งว่าจะให้ปรากฏเมื่อไหร่ — ในเกมจริงถูกต้องครับ
แต่ในฉากทดสอบไม่มีเซิร์ฟเวอร์ พอกด Play วัตถุที่ Interact ได้ก็หายหมด เพื่อนในทีมทดสอบงานตัวเองไม่ได้เลยถ้าผมไม่เปิดห้องให้ก่อน

🔇 ▸ *ช่วงที่วัตถุยังอยู่ครบและผู้เล่นทดสอบถูกวางลงฉาก — เงียบ 2–3 วินาที แล้วชี้*

ในคลิปนี้วัตถุอยู่ครบ และระบบวางผู้เล่นทดสอบให้เองครับ

▸ *ถ้าคลิปมีช่วงเลือดลด ให้ชี้ Console*

และตรงนี้เป็นดาเมจจริง เลือดลดลงตามลำดับ ระบบเลือดและระบบเซฟทำงานทั้งคู่

▶ **หยุดวิดีโอ**

▸ *ย่อหน้าปิด — ส่วนที่น่าจำที่สุดของสไลด์ พูดช้า ตรงไปตรงมา*

เรื่องนี้แก้สามรอบครับ
สองรอบแรกพลาดเพราะผมเข้าใจผิดว่า Mirror ปิดวัตถุ **ก่อน** Awake แต่จริง ๆ ปิด **หลัง** Awake โค้ดผมจึงทำงานเร็วเกินไปและไม่เจออะไรเลย
ผมไปอ่าน Source Code ของ Mirror เองถึงเจอสาเหตุ

จบส่วนของ GameDev ครับ

---

## ถ้าเวลาไม่พอ — ตัดตามลำดับนี้

1. คอลัมน์ซ้ายสไลด์ 6 ย่อเหลือ: *"ระบบเซฟเข้ารหัส AES-256 เสร็จแล้วครับ"* — ประหยัด 20 วิ
2. ตัดย่อหน้า "สัปดาห์ก่อนยังไม่สำเร็จ" ท้ายสไลด์ 7 — ประหยัด 20 วิ
3. เล่นวิดีโอสไลด์ 8 แค่ 15 วินาที แล้วเล่าปากเปล่า — ประหยัด 20 วิ

**ห้ามตัด:** ช่วงเงียบตอนผู้เล่นคนที่สองปรากฏ (สไลด์ 7) และย่อหน้า Server Authority (สไลด์ 6)

---
---

# English version

## ⚠ Before presentation day

Your three recordings total **172 seconds**; this whole section gets **240**. Playing them in full leaves you no time to speak.

1. **Trim each embedded video to 30–35 seconds max.** If you can't trim, note the seek timestamps and rehearse jumping to them.
2. **Mute the videos.** Unity editor audio adds nothing and you'll be talking over it.
3. **Test playback on the actual presentation machine.**
4. **Keep the raw mp4s in a folder as backup** in case the embed won't load.
5. **Click play fast and confidently.** Ten seconds of hunting for the play button kills the whole section's rhythm.

## SLIDE 6 — GameDev (1:30)

▸ *Don't read all 16 bullets. That's three minutes on its own.*

This is the GameDev section. One thing to take away: **the game is now genuinely playable online between two machines** — and I'll show you the real thing in a moment.

▸ *Left column — move through this quickly.*

Left column, **Foundations** — AES-256 encrypted local save, can't be edited in Notepad, loads automatically before the first scene, permanent items capped at one and lost on drop or death.

▸ *Middle column — slow down, this is the substance.*

Middle column, **Networking**, is the main work this week. Mirror and Epic Online Services integrated, build passing with no errors. Create a room, find rooms, join by Epic Product User ID — **no IP address, no port forwarding.** For a party game that's critical: if inviting friends is hard, the game ends there.

▸ *Right column.*

Right column, **Gameplay** — first-person movement and camera, interaction with doors and levers, health and damage, downed-and-revive with a timer synced between players.

▸ *Point at the last bullet. Slow and clear — heaviest line on the slide.*

The last line matters. All of it was rewritten to run under **Server Authority** — the server decides everything, a client can only *ask*, never declare that something happened. Our game needs that, because the rules create no pressure at all if they aren't the same for everyone.

Let me show you the real thing.

## SLIDE 7 — Testing: creating and joining a room (1:00)

▸ *Say both lines before pressing play, so the panel knows what to watch for.*

This is footage from a real test — **two machines, not two windows on one machine.** What I want you to watch for is the second player appearing in the same scene.

▶ **PRESS PLAY**

▸ *Talk over the opening.*

Create Room is opening a lobby on Epic and making this machine the host. The room's values — map, difficulty, player count — go up with the lobby. The other machine searches, then joins through the Epic Product User ID.

🔇 ▸ *When the second player appears — **stop talking.** Let it land for 3–4 seconds, then point at the screen.*

There. Two players in one scene, both moving, seeing each other.

▶ **STOP THE VIDEO** — don't let it run to the end.

▸ *Closing line — say this one.*

This didn't work last week. The client connected but no player appeared on the other screen. The cause was a prefab registration mismatch between the two machines: a server can create a player from a prefab it holds directly, but a client can only create one that's been registered.

## SLIDE 8 — Testing: TestWalk and offline mode (1:20)

▸ *Signal clearly that this is a different thing.*

This clip is the gameplay test scene — a different thing from the last one. This is **offline mode**.

▶ **PRESS PLAY**

▸ *Talk over the opening.*

Mirror automatically disables every scene object carrying a NetworkIdentity so the server controls when it appears. In a real match that's correct. But a test scene has no server — so pressing Play made every interactable object vanish, and my teammate couldn't test his own work unless I hosted a room for him first.

🔇 ▸ *When the props are visibly intact and the test player spawns — silence for 2–3 seconds, then point.*

Here the objects are all present, and the system placed a test player on its own.

▸ *If the clip includes the health drop, point at the console.*

And this is real damage — health stepping down. The health system and the save system are both working.

▶ **STOP THE VIDEO**

▸ *Closing paragraph — the memorable part. Slow and direct.*

This took three attempts. The first two failed because I assumed Mirror disables those objects **before** Awake — it actually happens **after**, so my code ran too early and found nothing at all. I only found the cause by reading Mirror's own source code.

That's the end of the GameDev section.

## If you run over — cut in this order

1. Compress slide 6's left column to one sentence: *"The AES-256 encrypted save system is done."* — saves 20 s
2. Cut the "this didn't work last week" paragraph at the end of slide 7 — saves 20 s
3. Play only 15 seconds of slide 8's video and narrate the rest — saves 20 s

**Never cut:** the silent beat when the second player appears (slide 7), and the Server Authority paragraph (slide 6).
