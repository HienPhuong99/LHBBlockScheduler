"""
Đóng gói bản phát hành trên máy KHÔNG có Windows / PowerShell (phiên cloud), đúng các bước build.ps1.

  python3 tools/pack_cloud.py stage           -> Dist/LHBBlockScheduler: DLL + PDB, LHB.lsp đã ghi MD5 + phiên bản,
                                                 build-info.txt (UTF-8 BOM, CRLF). In MD5 DLL.
  python3 tools/pack_cloud.py zip 20260930    -> Dist/LHBBlockScheduler_<ngày>_v<phiên bản>_Premium.zip gồm thư mục add-in
                                                 + HUONG_DAN_SU_DUNG.md + TINH_NANG_PREMIUM.md + hướng dẫn test của bản.

Thứ tự (yêu cầu user 30/09/2026 "cứ push code và nén luôn, tôi chỉ việc vô file kiểm tra"):
build Release tại commit đã chốt mã -> stage -> ghi MD5 vào Dist/HUONG_DAN_TEST_<ngày>_v<phiên bản>_Premium.md -> zip
-> commit hướng dẫn test + zip, push. Phiên bản lấy từ MyApp.Version (Properties/AssemblyInfo.cs).
"""
import datetime
import hashlib
import os
import re
import shutil
import sys
import zipfile

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BIN = os.path.join(REPO, "bin", "Release", "net48")
DIST = os.path.join(REPO, "Dist", "LHBBlockScheduler")


def version():
    text = open(os.path.join(REPO, "Properties", "AssemblyInfo.cs"), encoding="utf-8").read()
    m = re.search(r'public const string Version = "([^"]+)"', text)
    if not m:
        sys.exit("Không đọc được MyApp.Version trong Properties/AssemblyInfo.cs")
    return m.group(1)


def dll_md5(path):
    return hashlib.md5(open(path, "rb").read()).hexdigest().upper()


def stage():
    ver = version()
    dll = os.path.join(BIN, "LHBBlockScheduler.dll")
    if not os.path.exists(dll):
        sys.exit(f"Chưa có {dll} - build Release trước")
    shutil.rmtree(DIST, ignore_errors=True)
    os.makedirs(DIST)
    for f in os.listdir(BIN):
        # LHBLoader chỉ dùng trên máy build (đọc %APPDATA%\...\Runtime) -> không đưa vào bản phân phối
        if not f.startswith("LHBLoader."):
            shutil.copy2(os.path.join(BIN, f), DIST)
    data = open(os.path.join(DIST, "LHBBlockScheduler.dll"), "rb").read()
    md5 = hashlib.md5(data).hexdigest().upper()
    now = datetime.datetime.utcnow().strftime("%Y-%m-%d %H:%M:%S")
    info = f"Configuration: Release\r\nBuildTime: {now}\r\nDllSize: {len(data)}\r\nMD5: {md5}"
    open(os.path.join(DIST, "build-info.txt"), "wb").write(b"\xef\xbb\xbf" + info.encode("utf-8"))
    # Thay theo byte, giữ nguyên mã hoá UTF-8 của file LISP (như build.ps1)
    lsp = open(os.path.join(REPO, "LHB.lsp"), "rb").read()
    if b"@@LHB_BUILD_MD5@@" not in lsp or b"@@LHB_VERSION@@" not in lsp:
        sys.exit("LHB.lsp thiếu chữ mẫu @@LHB_BUILD_MD5@@ / @@LHB_VERSION@@")
    lsp = lsp.replace(b"@@LHB_BUILD_MD5@@", md5.encode()).replace(b"@@LHB_VERSION@@", f"v{ver} Premium".encode())
    open(os.path.join(DIST, "LHB.lsp"), "wb").write(lsp)
    print(f"v{ver} Premium: MD5 DLL {md5}, {len(data)} byte -> {DIST}")


def make_zip(date):
    ver = version()
    dll = os.path.join(DIST, "LHBBlockScheduler.dll")
    if not os.path.exists(dll):
        sys.exit("Chưa có Dist/LHBBlockScheduler - chạy 'stage' trước")
    md5 = dll_md5(dll)
    guide = os.path.join(REPO, "Dist", f"HUONG_DAN_TEST_{date}_v{ver}_Premium.md")
    if not os.path.exists(guide):
        sys.exit(f"Chưa có hướng dẫn test {guide}")
    # Hướng dẫn test phải ghi đúng MD5 của DLL trong zip (tránh gửi hướng dẫn ghi MD5 bản build khác)
    if md5 not in open(guide, encoding="utf-8").read():
        sys.exit(f"Hướng dẫn test chưa ghi MD5 {md5} của DLL đang đóng gói")
    zpath = os.path.join(REPO, "Dist", f"LHBBlockScheduler_{date}_v{ver}_Premium.zip")
    if os.path.exists(zpath):
        os.remove(zpath)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for f in sorted(os.listdir(DIST)):
            z.write(os.path.join(DIST, f), "LHBBlockScheduler/" + f)
        for f in ["HUONG_DAN_SU_DUNG.md", "TINH_NANG_PREMIUM.md"]:
            z.write(os.path.join(REPO, f), f)
        z.write(guide, os.path.basename(guide))
    print(f"{zpath} ({os.path.getsize(zpath)} byte), MD5 DLL {md5}")


if __name__ == "__main__":
    if len(sys.argv) >= 2 and sys.argv[1] == "stage":
        stage()
    elif len(sys.argv) >= 3 and sys.argv[1] == "zip":
        make_zip(sys.argv[2])
    else:
        sys.exit(__doc__)
