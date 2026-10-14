# Ghost Manifestation — standalone VFX

เอฟเฟกต์ควันสีเทาอมฟ้าหม่นสำหรับผีปรากฏตัวและหายตัว ใช้ Unity Particle System และ URP ไม่มีภาพ texture ภายนอก

## เปิดดู

1. เปิด `VFX_Preview.unity` ในโฟลเดอร์นี้ หรือเมนู **Tools > Pre-Thesis > VFX > Open Ghost Preview**
2. กด **Play**: ซ้ายคือปรากฏตัว ขวาคือหายตัว ระบบเล่นซ้ำทุก 5 วินาที
3. **Replay both** เล่นใหม่, **Auto replay** เปิด/ปิดการวน, **Show size reference** เปิด/ปิดหุ่นอ้างอิงขนาด

หุ่นเป็นเพียงตัวอ้างอิงสูงประมาณ 1.8 เมตร ไม่ใช่โมเดลผีจริง และการเปิด/ปิดหุ่นอยู่ในสคริปต์พรีวิวเท่านั้น

ดูโดยไม่เข้า Play Mode ได้: เปิด Prefab เลือกตัวรากที่มี Particle System แล้วใช้ปุ่ม Play/Restart ใน Scene View เปิดการแสดงลูกด้วยหาก Unity แสดงเฉพาะระบบที่เลือก

## ไฟล์ที่ใช้

- `Prefabs/Ghost_Appear.prefab`: ควันรอบตัวเคลื่อนเข้าด้านใน พร้อมไอและเถ้าบาง ๆ
- `Prefabs/Ghost_Disappear.prefab`: ควันแตกออกและลอยขึ้น ก่อนจางหาย
- `Materials/GhostSmoke.mat` และ `GhostSmoke.shader`: ควันแบบโปร่งใส ขอบนุ่ม มีลาย noise
- `Scripts/GhostVfxPreview.cs`: ปุ่มพรีวิวและหุ่นอ้างอิง ใช้เฉพาะซีนทดลอง

แต่ละ Prefab มี 4 ชั้น: BodyMist, TornVeil, GroundBreath, Ash รวมประมาณ 221 อนุภาคต่อการเล่น ไม่มีแสงจริง การชน หรือเสียง เอฟเฟกต์เป็น one-shot และจางหมดภายในประมาณ 3 วินาที ตำแหน่งรากคือระดับเท้า

## ปรับแต่งใน Inspector

- **Main > Start Color**: สีและความทึบของแต่ละชั้น
- **Main > Start Size / Start Lifetime**: ขนาดและอายุควัน
- **Emission > Bursts**: จำนวนอนุภาค
- **Velocity over Lifetime > Radial**: ค่าลบดึงเข้าด้านใน ค่าบวกกระจายออก
- **Noise**: ความปั่นป่วน
- **GhostSmoke material > Density**: ความหนาแน่นของภาพควันทั้งสอง Prefab

ตัวรากไม่มีอนุภาค ใช้เป็นจุดสั่งเล่นทั้งกลุ่ม หากปรับภาพ ให้เลือกชั้นลูก

ยังไม่มีการเชื่อมกับผี เกมเพลย์ หรือเครือข่าย และไม่ได้เพิ่มซีนนี้เข้า Build Settings การนำไปใช้ในเกมภายหลังต้องเชื่อมจังหวะสั่งเล่นกับระบบผี

## Authoring

`Editor/GhostVfxAuthoring.cs` สร้าง asset ในโปรเจกต์ทดลองว่างที่ใช้ Unity 6000.5.7f1 + URP เท่านั้น ไม่ทำงานอัตโนมัติเมื่อ import และปฏิเสธการสร้างทับซีนที่มีอยู่แล้ว ผู้ใช้ปรับ Prefab ที่สร้างไว้ได้โดยตรง ไม่ต้องรันตัวสร้างใหม่
