# local-transfer

local-transfer is a fully local, two-way file transfer application for iPhone Safari and Windows. It transfers photos, videos, and general files without Bluetooth, a cloud account, or an internet connection. The phone and computer only need to be connected to the same local Wi-Fi network; that network does not need internet access.

## Features

- Send photos, videos, and files from iPhone to Windows through Safari.
- Share files from Windows back to iPhone for download.
- Send and copy plain text in either direction without creating a file.
- Scan a temporary QR code instead of typing an address.
- Choose any folder for incoming files.
- Switch between light and dark themes.
- Use 18 built-in languages on both the desktop and mobile interfaces.
- Run completely locally with no analytics, cloud upload, or external web requests.

## Supported languages

Turkish, English, German, French, Spanish, Italian, Brazilian Portuguese, Dutch, Polish, Russian, Ukrainian, Hindi, Indonesian, Vietnamese, Japanese, Korean, Simplified Chinese, and Traditional Chinese.

local-transfer automatically starts with the Windows display language when supported. A language can also be selected at any time from the desktop header or the Safari page. Language files are bundled with the application and never require an internet connection.

## Installation and use

1. Double-click `local-transfer-Setup.exe` and approve the Windows administrator prompt.
2. When setup finishes, scan the QR code in the local-transfer window with the iPhone Camera app.
3. To send files to the computer, choose **Photos and videos** or **Choose file** in Safari.
4. To send files to the phone, select **Choose files** in the desktop application. The files appear under **From your computer** in Safari.
5. Use **Text transfer** to send written text in either direction and copy it on the receiving device.
6. Incoming files are saved to `Downloads\local-transfer` by default. Use **Change folder** to choose another location.

The theme, interface language, and incoming-file folder are saved for future sessions. Shared text is held only in memory for the current application session. A new access token is generated whenever the application starts. Existing files are never overwritten; duplicate names receive an automatic `(1)`, `(2)`, and so on. The per-file limit is 10 GB and the per-message text limit is 100,000 characters.

## Privacy and network use

- The running application makes no requests to external internet addresses.
- The mobile interface, icons, and language catalog are embedded in the Windows application.
- Transfers go directly through a local address such as `192.168.x.x` or `10.x.x.x`.
- Only devices holding the temporary token encoded in the QR code can access the transfer session.
- Local Wi-Fi traffic does not use an internet data allowance.

## Build from source

Requirements: Windows 10/11 x64 and the .NET 10 SDK. The first NuGet restore on a development computer can require internet access; the built application does not.

```powershell
dotnet restore .\src\LocalTransfer\local-transfer.csproj
powershell -ExecutionPolicy Bypass -File .\build\Build-Setup.ps1
```

The self-contained installer is created at `artifacts\local-transfer-Setup.exe`. The target computer does not need a separate .NET installation.

## Technical overview

The QR code contains a local IP address and a random session token, not the file data itself. Files stream directly over local Wi-Fi so large transfers do not need to be encoded into QR images. The installer limits the Windows Firewall rule to the application, the local subnet, and TCP ports 47831–47850.

## Third-party software

See [`THIRD-PARTY-NOTICES.txt`](THIRD-PARTY-NOTICES.txt) for dependency licenses and notices.
