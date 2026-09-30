"""
Đóng gói bản phát hành trên máy KHÔNG có Windows / PowerShell (phiên cloud), đúng các bước build.ps1.

  python3 tools/pack_cloud.py stage           -> Dist/LHBBlockScheduler: 3 bản DLL + PDB (v9.7: thư mục gốc = AutoCAD
                                                 2021 - 2024, net8 = 2025 - 2026, net10 = 2027), mỗi bản 1 build-info.txt
                                                 (UTF-8 BOM, CRLF), LHB.lsp đã ghi 3 MD5 + phiên bản. In MD5 từng DLL.
  python3 tools/pack_cloud.py zip 20260930    -> Dist/LHBBlockScheduler_<ngày>_v<phiên bản>_Premium.zip gồm thư mục add-in
                                                 + HUONG_DAN_SU_DUNG.md + TINH_NANG_PREMIUM.md + hướng dẫn test của bản.

Thứ tự (yêu cầu user 30/09/2026 "cứ push code và nén luôn, tôi chỉ việc vô file kiểm tra"):
build Release cả 3 đích (-p:LhbAllTargets=true) tại commit đã chốt mã -> stage -> ghi 3 MD5 vào
Dist/HUONG_DAN_TEST_<ngày>_v<phiên bản>_Premium.md -> zip -> commit hướng dẫn test + zip, push.
Phiên bản lấy từ MyApp.Version (Properties/AssemblyInfo.cs).
"""
import datetime
import hashlib
import os
import re
import shutil
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(REPO, "Dist", "LHBBlockScheduler")

# (thư mục con trong gói, thư mục build bin/Release/<...>, chữ mẫu MD5 trong LHB.lsp, dòng AutoCAD)
TARGETS = [
    ("", "net48", b"@@LHB_BUILD_MD5@@", "AutoCAD 2021 - 2024"),
    ("net8", "net8", b"@@LHB_BUILD_MD5_NET8@@", "AutoCAD 2025 - 2026"),
    ("net10", "net10", b"@@LHB_BUILD_MD5_NET10@@", "AutoCAD 2027"),
]


def version():
    text = open(os.path.join(REPO, "Properties", "AssemblyInfo.cs"), encoding="utf-8").read()
    m = re.search(r'public const string Version = "([^"]+)"', text)
    if not m:
        sys.exit("Không đọc được MyApp.Version trong Properties/AssemblyInfo.cs")
    return m.group(1)


def dll_md5(path):
    return hashlib.md5(open(path, "rb").read()).hexdigest().upper()


def target_dir(sub):
    return DIST if sub == "" else os.path.join(DIST, sub)


def stage():
    ver = version()
    for sub, binname, _, name in TARGETS:
        dll = os.path.join(REPO, "bin", "Release", binname, "LHBBlockScheduler.dll")
        if not os.path.exists(dll):
            sys.exit(f"Chưa có {dll} ({name}) - build Release với -p:LhbAllTargets=true trước")
    shutil.rmtree(DIST, ignore_errors=True)
    now = datetime.datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
    md5s = {}
    for sub, binname, _, name in TARGETS:
        src = os.path.join(REPO, "bin", "Release", binname)
        dst = target_dir(sub)
        os.makedirs(dst, exist_ok=True)
        for f in os.listdir(src):
            p = os.path.join(src, f)
            # LHBLoader chỉ dùng trên máy build (đọc %APPDATA%\...\Runtime) -> không đưa vào bản phân phối
            if os.path.isfile(p) and not f.startswith("LHBLoader."):
                shutil.copy2(p, dst)
        data = open(os.path.join(dst, "LHBBlockScheduler.dll"), "rb").read()
        md5 = hashlib.md5(data).hexdigest().upper()
        md5s[sub] = md5
        info = f"Configuration: Release\r\nBuildTime: {now}\r\nDllSize: {len(data)}\r\nMD5: {md5}"
        open(os.path.join(dst, "build-info.txt"), "wb").write(b"\xef\xbb\xbf" + info.encode("utf-8"))
        print(f"  {name:<20} {('LHBBlockScheduler/' + sub).rstrip('/'):<28} MD5 {md5}, {len(data)} byte")
    # Thay theo byte, giữ nguyên mã hoá UTF-8 của file LISP (như build.ps1)
    lsp = open(os.path.join(REPO, "LHB.lsp"), "rb").read()
    for sub, _, placeholder, _ in TARGETS:
        if placeholder not in lsp:
            sys.exit(f"LHB.lsp thiếu chữ mẫu {placeholder.decode()}")
        lsp = lsp.replace(placeholder, md5s[sub].encode())
    if b"@@LHB_VERSION@@" not in lsp:
        sys.exit("LHB.lsp thiếu chữ mẫu @@LHB_VERSION@@")
    lsp = lsp.replace(b"@@LHB_VERSION@@", f"v{ver} Premium".encode())
    open(os.path.join(DIST, "LHB.lsp"), "wb").write(lsp)
    print(f"v{ver} Premium -> {DIST}")


def make_zip(date):
    ver = version()
    md5s = {}
    for sub, _, _, name in TARGETS:
        dll = os.path.join(target_dir(sub), "LHBBlockScheduler.dll")
        if not os.path.exists(dll):
            sys.exit(f"Chưa có {dll} ({name}) - chạy 'stage' trước")
        md5s[sub] = dll_md5(dll)
    guide = os.path.join(REPO, "Dist", f"HUONG_DAN_TEST_{date}_v{ver}_Premium.md")
    if not os.path.exists(guide):
        sys.exit(f"Chưa có hướng dẫn test {guide}")
    # Hướng dẫn test phải ghi đúng MD5 của cả 3 DLL trong zip (tránh gửi hướng dẫn ghi MD5 bản build khác)
    text = open(guide, encoding="utf-8").read()
    missing = [f"{name} ({md5s[sub]})" for sub, _, _, name in TARGETS if md5s[sub] not in text]
    if missing:
        sys.exit("Hướng dẫn test chưa ghi MD5 DLL đang đóng gói: " + ", ".join(missing))
    zpath = os.path.join(REPO, "Dist", f"LHBBlockScheduler_{date}_v{ver}_Premium.zip")
    if os.path.exists(zpath):
        os.remove(zpath)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for root, dirs, files in os.walk(DIST):
            dirs.sort()
            for f in sorted(files):
                full = os.path.join(root, f)
                rel = os.path.relpath(full, DIST).replace(os.sep, "/")
                z.write(full, "LHBBlockScheduler/" + rel)
        for f in ["HUONG_DAN_SU_DUNG.md", "TINH_NANG_PREMIUM.md"]:
            z.write(os.path.join(REPO, f), f)
        z.write(guide, os.path.basename(guide))
    print(f"{zpath} ({os.path.getsize(zpath)} byte)")
    for sub, _, _, name in TARGETS:
        print(f"  {name:<20} MD5 {md5s[sub]}")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "stage":
        stage()
    elif len(sys.argv) >= 3 and sys.argv[1] == "zip":
        make_zip(sys.argv[2])
    else:
        sys.exit(__doc__)
