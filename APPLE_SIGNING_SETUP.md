# Apple Signing Setup — ship MAX to your iPhone via TestFlight (YT-99)

This is the one-time setup so the **iOS → TestFlight** GitHub Action can build and install
MAX on your iPhone. You do **not** need a Mac. Follow it top to bottom; every value you create
here becomes a GitHub secret at the end.

**What you need first:** an **Apple Developer Program** membership (US$99/yr) —
<https://developer.apple.com/programs/>. Sign in to both sites with that account:
- **Developer portal:** <https://developer.apple.com/account> (certificates, identifiers, profiles)
- **App Store Connect:** <https://appstoreconnect.apple.com> (the app record + API key)

The commands below are **Windows PowerShell** (you have OpenSSL via Git for Windows at
`C:\Program Files\Git\usr\bin\openssl.exe`, or install it). Run them in a scratch folder.

---

## A. Register the App ID (bundle id)

1. Developer portal → **Certificates, Identifiers & Profiles** → **Identifiers** → **+**.
2. Type **App IDs** → **App**.
3. **Bundle ID: Explicit** → `com.codynamics.maxvstheworlds` (must match `ProjectSettings` →
   iPhone bundle id — CC proposed this in YT-98; tell CC if you want a different one).
4. Description: `MAX vs THE WORLDS`. Leave capabilities default. **Continue → Register.**

Note your **Team ID** while here: top-right of the portal, or Membership page — a 10-char code
like `AB12CD34EF`. → secret **`APPLE_TEAM_ID`**.

---

## B. Distribution certificate → `.p12` (done on Windows with OpenSSL)

A certificate = a keypair Apple signs. You generate the key + a CSR locally, Apple returns a
`.cer`, and you bundle key+cer into a `.p12`.

```powershell
# 1. Private key + Certificate Signing Request
openssl genrsa -out ios_dist.key 2048
openssl req -new -key ios_dist.key -out ios_dist.csr -subj "/emailAddress=lee@codynamics.com.au/CN=MAX Distribution/C=AU"
```

2. Developer portal → **Certificates** → **+** → **Apple Distribution** → **Continue**.
3. Upload **`ios_dist.csr`** → **Continue** → **Download** the `distribution.cer`.

```powershell
# 4. Convert Apple's .cer (DER) to PEM, then bundle with your key into a .p12.
#    Choose any export password and REMEMBER it — it becomes a secret.
openssl x509 -in distribution.cer -inform DER -out ios_dist.pem -outform PEM
openssl pkcs12 -export -inkey ios_dist.key -in ios_dist.pem -out ios_dist.p12 -name "MAX Distribution" -passout pass:CHOOSE_A_PASSWORD
```

- `ios_dist.p12` → secret **`IOS_DIST_CERT_P12_BASE64`** (base64 — see §F).
- the password you chose → secret **`IOS_DIST_CERT_PASSWORD`**.

---

## C. App Store provisioning profile → `.mobileprovision`

1. Developer portal → **Profiles** → **+**.
2. **Distribution → App Store Connect** → **Continue**.
3. **App ID:** pick `com.codynamics.maxvstheworlds` → **Continue**.
4. **Certificate:** pick the *MAX Distribution* cert from §B → **Continue**.
5. **Name it** exactly, e.g. `MAX AppStore` → **Generate** → **Download** the
   `MAX_AppStore.mobileprovision`.

- the file → secret **`IOS_PROVISIONING_PROFILE_BASE64`** (base64 — see §F).
- the **name** you typed (`MAX AppStore`) → secret **`IOS_PROVISIONING_PROFILE_NAME`** (exact text).

---

## D. App Store Connect API key → `.p8` + key id + issuer id

This lets CI upload without your Apple ID or 2FA.

1. App Store Connect → **Users and Access** → **Integrations** tab → **App Store Connect API**.
2. **+** to generate a key. Name: `MAX CI`. Access role: **App Manager**. **Generate.**
3. **Download** `AuthKey_XXXXXXXXXX.p8` (you can only download it **once** — keep it safe).
4. On that page note:
   - **Issuer ID** (a UUID, shown above the keys list) → secret **`APP_STORE_CONNECT_ISSUER_ID`**.
   - the key's **Key ID** (the 10-char `XXXXXXXXXX`) → secret **`APP_STORE_CONNECT_KEY_ID`**.
   - the `.p8` file → secret **`APP_STORE_CONNECT_P8_BASE64`** (base64 — see §F).

---

## E. Create the app record in App Store Connect

1. App Store Connect → **Apps** → **+** → **New App**.
2. **Platform:** iOS. **Name:** `MAX vs THE WORLDS` (or as you like — must be unique on the store).
3. **Bundle ID:** select `com.codynamics.maxvstheworlds` (from §A). **SKU:** any string, e.g.
   `maxvstheworlds`. **Create.**

You don't need to fill store metadata for TestFlight — just the app record must exist so the
upload has somewhere to land.

---

## F. Encode the files and add the GitHub secrets

Three secrets are files that must be **base64** (single line). In PowerShell:

```powershell
# Prints the base64 to the console AND copies it to your clipboard.
function ToB64($path) { $s = [Convert]::ToBase64String([IO.File]::ReadAllBytes($path)); $s | Set-Clipboard; $s.Length }
ToB64 "ios_dist.p12"                     # -> paste into IOS_DIST_CERT_P12_BASE64
ToB64 "MAX_AppStore.mobileprovision"     # -> paste into IOS_PROVISIONING_PROFILE_BASE64
ToB64 "AuthKey_XXXXXXXXXX.p8"            # -> paste into APP_STORE_CONNECT_P8_BASE64
```

Then in GitHub: **repo → Settings → Secrets and variables → Actions → New repository secret**,
and add each of these:

| Secret name | Value |
|---|---|
| `APPLE_TEAM_ID` | 10-char Team ID (§A) |
| `IOS_BUNDLE_ID` | `com.codynamics.maxvstheworlds` |
| `IOS_DIST_CERT_P12_BASE64` | base64 of `ios_dist.p12` (§B) |
| `IOS_DIST_CERT_PASSWORD` | the `.p12` export password (§B) |
| `IOS_PROVISIONING_PROFILE_BASE64` | base64 of the `.mobileprovision` (§C) |
| `IOS_PROVISIONING_PROFILE_NAME` | the profile's exact name, e.g. `MAX AppStore` (§C) |
| `APP_STORE_CONNECT_ISSUER_ID` | Issuer ID UUID (§D) |
| `APP_STORE_CONNECT_KEY_ID` | 10-char Key ID (§D) |
| `APP_STORE_CONNECT_P8_BASE64` | base64 of the `.p8` (§D) |

The Unity secrets (`UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`) already exist — they power
the WebGL build too. Nothing here is ever committed to the repo.

---

## G. Run it

- **GitHub → Actions → “iOS → TestFlight” → Run workflow**, or push a version tag:
  ```powershell
  git tag v0.1.0 ; git push origin v0.1.0
  ```
- The **build-ios** job (Linux) makes the Xcode project; **release-ios** (macOS) signs it and
  uploads to TestFlight. Until the secrets above exist, `release-ios` stops at the **Preflight**
  step and tells you exactly which secrets are missing — that's expected.
- First upload takes ~10 min for Apple to process. Then: App Store Connect → your app →
  **TestFlight** → add yourself as an **Internal Tester**, install the **TestFlight** app on your
  iPhone, and the build appears there.

---

## Troubleshooting

- **Preflight fails listing secrets** → one or more of §F isn't set (or has a typo in the name).
- **“No signing certificate / profile matches”** → the profile (§C) must be built from the *same*
  cert (§B) and the *same* bundle id (§A); the `IOS_PROVISIONING_PROFILE_NAME` secret must match
  the profile's name **exactly**.
- **Upload auth fails** → the API key (§D) needs the **App Manager** role, and the app record (§E)
  must exist with the matching bundle id.
- **Wrong Xcode / build error on the mac job** → the workflow selects `latest-stable` Xcode; if a
  future Unity needs a specific version, pin `xcode-version:` in `.github/workflows/ios-testflight.yml`.
