# VPSReady

### จัดการพื้นฐานของเซิร์ฟเวอร์ Ubuntu จากแอปบนเดสก์ท็อป

[English](README.md) · [คู่มือใช้งาน](docs/user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md) · [ศูนย์รวมเอกสาร](docs/README.md) · [สถานะโปรเจกต์](docs/PROJECT_STATUS.md)

> [!IMPORTANT]
> **ยังเป็น pre-release ไม่ใช่เวอร์ชัน stable** โค้ดแก้ไขอยู่บน `release/0.1.0`
> เพื่อให้ตรวจทาน แต่การตรวจบน hosted CI ยังค้างอยู่ การมี release branch
> ไม่ได้แปลว่าได้รับอนุมัติให้เริ่มทดสอบกับ VPS จริงแล้ว
> ตรวจ [สถานะและหลักฐาน](docs/PROJECT_STATUS.md) ก่อนใช้งาน
> **REAL VPS: NOT TESTED.**

## โปรเจกต์นี้ทำอะไร

VPSReady เป็นแอป C#/.NET และ Avalonia สำหรับ Windows, macOS และ Linux
ใช้ SSH จัดการเซิร์ฟเวอร์ **Ubuntu** โดยไม่ติดตั้ง daemon ของ VPSReady
ค้างไว้บนเซิร์ฟเวอร์ ไม่ต้องมีบัญชีบริการส่วนกลาง ไม่มี telemetry
และไม่อัปโหลดข้อมูลวินิจฉัยอัตโนมัติ

## ขอบเขต v0.1 Core Basic

| งาน | ความสามารถและข้อควรทราบ |
| --- | --- |
| การเชื่อมต่อ | ทดสอบ SSH ด้วยรหัสผ่าน และให้ตรวจสอบ host key อย่างชัดเจนก่อนเชื่อถือ |
| ภาพรวม | แสดงสถานะการเชื่อมต่อและ session; UI ยังไม่แสดงข้อมูลเซิร์ฟเวอร์ทั้งหมดที่ตรวจพบ |
| Firewall | ตรวจ UFW เพิ่มกฎ TCP/UDP ลบกฎที่เลือก และเปิด/ปิด โดยป้องกันเส้นทาง SSH ที่ใช้งานอยู่และปฏิเสธกรณีที่วิเคราะห์ไม่แน่ชัด |
| SSH key | สร้าง ED25519 และติดตั้ง public key; ต้องทดสอบ key authentication ด้วยการเชื่อมต่อแยกอีกครั้ง |
| SSH config | สร้าง alias ในเครื่อง พร้อมตรวจชื่อซ้ำและรักษาเนื้อหาเดิม; config ที่ซับซ้อนอาจต้องแก้ด้วยตนเอง |
| ระบบ | รีเฟรชรายการแพ็กเกจ ตรวจแผนก่อนอัปเกรด รีบูต/เชื่อมต่อใหม่ เปลี่ยน hostname และ timezone |
| การวินิจฉัย | Activity, journal, รายงาน issue และ support bundle ที่ผ่านการปกปิดข้อมูลสำคัญ โดยเก็บในเครื่องจนกว่าจะเลือกแชร์ |

รายการนี้อธิบายความสามารถของโค้ด ไม่ใช่คำยืนยันว่าทดสอบบน VPS จริงแล้ว
ไม่มี distribution upgrade หรือการรีบูตเงียบ และการยกเลิกงานไม่ใช่ rollback

Docker, Coolify, Kubernetes, Fail2ban, web/database stack, DNS/TLS automation,
cloud-provider API และ remote terminal อเนกประสงค์ไม่อยู่ในขอบเขตรุ่นนี้
อ่านรายละเอียดใน [ข้อกำหนดผลิตภัณฑ์](docs/V0.1_CORE_BASIC_SPEC.md)

## เริ่มจาก source code

ติดตั้ง Git และ .NET SDK ตาม [global.json](global.json) แล้วรัน:

```bash
git clone --branch release/0.1.0 https://github.com/ZillionBuilds/VPSReady.git
cd VPSReady
dotnet restore VpsReady.slnx --locked-mode
dotnet build VpsReady.slnx --configuration Release --no-restore
dotnet run --project src/VpsReady.Desktop/VpsReady.Desktop.csproj --configuration Release --no-build
```

การ build และเปิดแอปโดยยังไม่เชื่อมต่อไม่ต้องใช้ข้อมูล VPS
แต่แอปจริงไม่ใช่ sandbox จำลองเซิร์ฟเวอร์ การเชื่อมต่อ VPS จริงต้องผ่านขั้นตอนอนุมัติ

แพ็กเกจ candidate เป็นแบบ self-contained จึงไม่ต้องติดตั้ง .NET runtime แยก
แต่ยังไม่ได้ลงลายเซ็น ตรวจระบบปฏิบัติการ สถาปัตยกรรม commit และ checksum
ให้ตรงกับหลักฐานทุกครั้ง อย่าใช้ผลทดสอบแพ็กเกจเก่ายืนยันแพ็กเกจใหม่

## ใช้งานอย่างระมัดระวัง

- ตรวจ fingerprint จากแหล่งอิสระก่อนยอมรับ host key หรือการเปลี่ยน key
- เตรียม SSH หรือช่องทางกู้คืนที่แยกจากแอปก่อนเปลี่ยนค่าที่กระทบการเข้าถึง
- เมื่อ timeout ยกเลิก หรือล้มเหลวบางส่วน ให้ตรวจสถานะจริง อย่าสรุปว่าคำสั่งถูกย้อนกลับแล้ว
- ห้ามแนบรหัสผ่าน private key token หรือ log/config ที่มีข้อมูลลับใน issue สาธารณะ
- ตรวจรายงานที่ปกปิดข้อมูลแล้วด้วยตนเองก่อนแชร์ แอปไม่ส่งข้อมูลให้อัตโนมัติ

อ่าน [คู่มือใช้งานและแก้ปัญหา](docs/user-guide/V0.1_USER_AND_TROUBLESHOOTING_GUIDE.md)
และ [ขั้นตอนทดสอบสำหรับ Owner](docs/owner-testing/OWNER_VPS_TEST_PROTOCOL.md)
ก่อนเริ่มทดสอบ candidate ที่ได้รับอนุมัติ

## อ่านต่อ

- [สถานะและหลักฐานการทดสอบ](docs/PROJECT_STATUS.md)
- [ศูนย์รวมเอกสาร](docs/README.md)
- [แนวทางร่วมพัฒนา](CONTRIBUTING.md)
- [ประวัติการเปลี่ยนแปลง](CHANGELOG.md)
- [สัญญาอนุญาต Apache 2.0](LICENSE)

หน้านี้เป็นบทนำภาษาไทย รายละเอียดทางเทคนิค ข้อกำหนดความปลอดภัย
และขั้นตอนในเอกสารภาษาอังกฤษที่เชื่อมโยงไว้เป็นแหล่งอ้างอิงหลัก
