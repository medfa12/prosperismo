<!--
Copyright (C) 2026 SharpEmu Emulator Project
SPDX-License-Identifier: GPL-2.0-or-later
-->

# PS5 React Native bundle map: which bundle is which app

The PS5 shell is a set of React Native apps, one per `NPXS4xxxx` title id. This
file maps every bundle we hold to the screen it implements, so anyone reaching
for "the real home layout" or "the real store layout" opens the right file first.

Nothing from `games/` is reproduced here. Only identifiers, build-workspace names
and certificate subjects are quoted, as evidence.

Two questions are answered separately, because their evidence quality differs
sharply:

1. **Identity**, which application is inside a given bundle file. This is now
   settled for all 63 bundles by the signing certificate, channel E1 below.
2. **Role**, which user-facing screen that application actually draws. This is
   what the confidence column rates. A bundle can have a certain identity and an
   uncertain role.

## Sources

| Short name | Location | What it is |
| --- | --- | --- |
| 4.03 tree | `games/PS5_4.03_reconstructed/filesystems/system_ex/rnps` | 61 app directories plus one `bgs` service directory, 63 bundle files, plaintext `manifest.json` beside each |
| 4.02 set | `games/rnps_4.02` | 19 standalone `RNPSPACK` packages named after their component |
| 3.00 JS | `games/useful rnps/readable_js_3.00` | 28 console-decrypted bundles recovered to readable JavaScript, keyed by NPXS id |
| 3.00 pairs | `games/useful rnps/{encrypted,decrypted}_bins_3.00` | the same 28 bundles as exact encrypted/decrypted pairs |

## Evidence channels

**E1, signing certificate CN (decisive, all 63 bundles).** Every `RNPSHEDR`
container carries an X.509 chain in cleartext ahead of the encrypted body:
intermediate certificate at header offset `0x0C`/`0x10`, leaf at `0x14`/`0x18`,
body at `0x1C`/`0x20`, true plaintext length at `0x24`. The leaf certificate is
issued per application and its subject is literally `CN=<application name>`. It is
readable without any key, it survives on the encrypted 4.02 packages, and it is
bound to the payload by the RSA-3072 trailer signature. This is a far stronger
identifier than the file name, which is only what a dumper chose to call the file.

**E2, plaintext `manifest.json` (strong, 4.03 only).** Each 4.03 app directory has
an unencrypted manifest giving `titleId`, `applicationName`, `applicationVersion`.
This is what ties an application name to an NPXS id.

**E3, embedded build paths in decrypted 3.00 JavaScript (strong, 28 apps).** The
recovered bundles keep Babel `fileName` strings of the form
`/home/jenkins/jenkins_slave/workspace/<job>/apps/<app>/src/modules/<screen>/`.
Jenkins truncates long job directory names from the left, so
`rnps-control-center_v2_...` appears as `rol-center_v2_...`; the tail
`_v2_ppr_releases_03.00` is the release train and is identical everywhere. The
app-local `src/` and `packages/` roots name the screens directly. This is the only
channel that shows what an app draws.

**E4, shipped `assets/` tree (strong where the tree is specific).** 23 of the 4.03
apps ship an extracted asset tree that mirrors the app's own source layout, so it
survives even though the bundle body does not. Where the filenames are specific
(payment-method logos, trophy icons, disc-region art) this pins the role.

**E5, `appdb/*/param.json` tiles (strong, 12 apps).** Where an app registers a
shell tile it ships the localized tile name and the hub URI. This is the only
place a user-facing English label appears.

**E6, `appName` literal (weak on its own).** Every bundle carries an `appName:`
literal used by telemetry. It is real (`appName: "elysion"`, `appName: "mf"`,
`appName: "artemis-psnow"`) but it is often a codename or an abbreviation, and
reading a feature out of it is how the earlier version of this document got five
apps wrong. It is quoted below as corroboration only, never as the basis.

Reproduce E3 with:

    python tools/rn-layout/extract_styles.py <bundle>.js --sources

Reproduce E2, E4 and E5 with:

    python scripts/rnps_catalog.py <dump-root>

### Why the 4.02 package bodies stay closed

`RNPSPACK` header: magic at `0x00`, container version `2` at `0x08`, header size
`0x280` at `0x10`, a 20-byte digest at `0x50`, then version major, minor, patch
and build as four little-endian words at `0x64`, `0x68`, `0x6C`, `0x70`, then the
NUL-padded application name at `0x74`. An `RNPSHEDR` bundle begins at offset 640.

> **Corrected and extended.** A survey of all **19** `.epkg` packages in
> `games/rnps_4.02`, all **62** bundles in the 4.03 dump and all **56** files of the
> 3.00 encrypted/decrypted pair set changes three readings above. See
> "The container, measured across 137 files" immediately below.
>
> | Claim above | Verdict |
> |---|---|
> | `0x10` is "header size `0x280`" | It is a **section offset**. `0x0C` is a **section count**, and `rnps-settings.epkg` has `0x0C = 2` with a second offset at `0x14 = 0xFFC400`, where a second `RNPSHEDR` really does begin. The other 18 packages have one section, so `0x280` is a header size only by coincidence. |
> | `0x64`..`0x70` are version major, minor, patch, build | **Not supported.** `0x64 = 4` in all 19 packages, so it cannot be a major version. `rnps-action-cards` and `rnps-control-center` carry identical `(0x68, 0x6C, 0x70)` at different sizes, and `0x70` correlates with nothing. **UNRESOLVED.** |
> | "An `RNPSHEDR` bundle begins at offset 640" | True **only for `.epkg`**. `RNPSPACK` is an OTA distribution envelope and exists on no other artefact: the 62 installed 4.03 bundles and all 56 3.00 bins begin **directly with `RNPSHEDR`** at offset 0. |

### The container, measured across 137 files

**EXACT.** Verified programmatically over 19 `.epkg`, 62 in-dump 4.03 bundles, 28
encrypted 3.00 bins and 28 decrypted 3.00 bins.

```
RNPSPACK  (OTA envelope, .epkg only, 0x280 bytes)
 0x00  "RNPSPACK"
 0x08  u32  = 2                     constant, 19/19
 0x0C  u32  = RNPSHEDR section count       1, except rnps-settings = 2
 0x10  u32  = offset of section 0          0x280, 19/19
 0x14  u32  = offset of section 1          0, except rnps-settings = 0xFFC400
 0x50  ff ff ff 04 + 16 bytes, unique per file      UNRESOLVED
 0x64  u32  = 4                     constant, 19/19
 0x68  u32  varies 0..0x10          \
 0x6C  u32  varies 0..6              > UNRESOLVED, see above
 0x70  u32  varies                  /
 0x74  NUL-padded ASCII application name
 0x100 384 bytes, uniform high entropy, suspected RSA-3072 signature

RNPSHEDR  (the real container; base B = 0x280 in an .epkg, 0 everywhere else)
 +0x00 "RNPSHEDR"
 +0x08 u32 = 2          constant across all 137 files, FW 3.00, 4.02 and 4.03 alike
 +0x0C u32 = 0x1D0      = 0x50 + 0x180
 +0x10 u32 = DER length of the intermediate certificate
 +0x14 u32 = +0x10 + 0x1D0
 +0x18 u32 = DER length of the leaf certificate
 +0x1C u32 = align16(0x50 + f10 + f18) + 0x180     total certificate + signature block
 +0x20 u32 = payload size rounded up to 16
 +0x24 u32 = payload size, exact
 +0x28 u32 = +0x20 + +0x1C
 +0x2C u32 = 1
 +0x180      0x50 bytes, high entropy                UNRESOLVED
 +0x1D0      intermediate certificate, X.509 DER, CLEARTEXT
 +0x1D0+f10  leaf certificate, X.509 DER, CLEARTEXT
 align16  -> 0x180 bytes, RSA-3072 signature
            encrypted payload, length +0x20
```

Invariants, each checked on every file in the set:

| Invariant | Holds on |
|---|---|
| `f28 = f20 + f1C` | 19/19 `.epkg` |
| `f14 = f10 + 0x1D0` | 137/137 |
| `f1C = align16(0x50 + f10 + f18) + 0x180` | 137/137 |
| `f20 = align16(f24)` | 137/137 |
| `file size = 0x180 + f28` | 62/62 in-dump 4.03, 28/28 encrypted 3.00, 28/28 decrypted 3.00 |
| `file size = 0x400 + f28` | 18/19 `.epkg`; the exception is the two-section `rnps-settings` |

### What the crypto actually covers

**EXACT**, from diffing the 3.00 encrypted and decrypted pairs for the same title id.
The first differing byte is at `0xB20` (NPXS40002, NPXS40015) or `0xB30` (NPXS40046),
which is exactly `align16(end of leaf certificate)`, the start of the signature. The
header and **both certificates are byte-identical** between the encrypted and decrypted
forms.

So the console-side operation covers **`[signature .. EOF]`** and nothing before it.
Everything structural, including the full certificate chain, is readable without any key.

Entropy confirms the same boundary. Measured in 4 KB windows (uniform-random expectation
is about 7.954 bits/byte at that window size, **not** 8):

| Region | Entropy |
|---|---|
| first 4 KB of the file, containing header + certificates | 7.641 to 7.658 |
| every window from the payload start onward | 7.947 to 7.962 |

The pre-payload region is high entropy too, but it is not ciphertext: it is DER-embedded
RSA moduli and signature values interleaved with cleartext ASN.1. Do not use a plain
entropy scan to find the payload boundary; compute it as `B + 0x180 + f1C`.

### The certificate chain, and the free title-id map

**EXACT.** Every file carries a two-certificate chain in cleartext DER:

| Certificate | Issuer | Subject |
|---|---|---|
| intermediate | `C=US, ST=California, O=SIE LLC, OU=Baymax CA, CN=SIE RNPS Root` | `CN=SIE RNPS Intermediate <N>`, N observed as 2, 4, 5, 6, 15, 24, 39 |
| leaf | the intermediate | **`CN=<application name>`** |

Signature algorithm OID `2a 86 48 86 f7 0d 01 01 0b`, `sha256WithRSAEncryption`.
Intermediate validity runs 2019-03-20 to 2121-11-20.

**This is the decisive point for the map below.** The leaf subject is the application
name, in cleartext, in the installed 4.03 bundles. So the NPXS id to application name
mapping is recoverable from a 4.03 dump **with no decryption at all**: NPXS40002 to
`rnps-home`, NPXS40003 to `rnps-control-center`, NPXS40008 to `rnps-settings`, and so on
for all 62.

### Incidental

- `games/useful rnps/ota_package/rnps-home.epkg` is **byte-identical** to
  `games/rnps_4.02/rnps-home.epkg`, SHA-256 `f379999924fad51c...`.
- The decrypted 3.00 payload is 95.9 % printable and is indexed by a
  `(u32 size, u32 offset)` table; `readable_js_3.00/*.js` is a re-extracted form of it,
  not a byte slice of the `.bin`.
- The 384-byte block at `RNPSPACK + 0x100` tests as uniform: chi-square against uniform
  is 208.0 to 260.0 on 255 degrees of freedom, where the expectation is 255 plus or minus
  22.6. No sub-window is structured. Its leading byte varies per file, so it is a raw
  big-endian signature value rather than a PKCS#1 v1.5 block in the clear.

The body is processed in 16-byte blocks with zero padding. ECB and a fixed reused
stream keystream are both ruled out by the 3.00 pair corpus; AES-CBC is the best
fit but is not proven. A printable-byte census over the first 200 KB past offset
4096 gives 0.370 for `rnps-home.epkg`, 0.370 for the encrypted 3.00
`NPXS40002.bin`, 0.370 for the 4.03 `NPXS40002/application.ps.bundle` and 0.975
for the decrypted 3.00 `NPXS40002.bin`. 0.370 is 95/256, exactly the uniform
random expectation, so the bodies carry no recoverable text. Every name, version
and certificate quoted below comes from the cleartext header region, never from
the body.

## The map

Sorted by confidence, high first. "Surface" is whether the bundle draws something
the user sees.

### High confidence

Identity from E1 plus a role established by decrypted source roots, a registered
tile, or an asset tree that could not belong to anything else.

| Bundle file | App / screen it implements | Evidence | Surface |
| --- | --- | --- | --- |
| `apps/NPXS40002/application.ps.bundle` | **rnps-home**, the Home screen: top nav, space switcher, game-icon row, options menu, strand rails | E1 `CN=rnps-home`; E2 `rnps-home` 4.1.0+12349; E3 job `rnps-home_v2_ppr_releases_03.00`, roots `packages/home-ui/src`, `packages/rnps-js-modules-strand/src`, `packages/rnps-js-modules-experience-options-menu/src`; 110 self-name tokens; references the library, explore and media-hub tile ids | Yes, full screen |
| `apps/NPXS40003/application.ps.bundle` | **rnps-control-center**, the PS-button bar and its panels | E1 `CN=rnps-control-center`; E2; E3 job `rol-center_v2_ppr_releases_03.00@2`, `apps/control-center` x131, roots `src/modules/control-center`, `src/modules/action-carousel`, `packages/function-control-{mic,sound,power,network,vr,music,broadcast,profile,apps,device,transfers}`, `packages/all-mute`; deep link `pscontrolcenter:main` | Yes, overlay |
| `apps/NPXS40036/application.ps.bundle` | **rnps-action-cards-host-app**, the host that runs each Control Center / Game Hub card | E1 `CN=rnps-action-cards-host-app`; E2 `rnps-action-cards`; E3 the same Jenkins job as NPXS40003, `apps/action-cards-host` x141, roots `packages/{create-group,broadcast,music,psnow-player,remoteplay,share-framework,video,webbrowser,real-time-communication,recent-capture,mini-profile,explore}`; E4 NPXS40003 also ships an `assets/action-cards-host/` subtree | Yes, overlay |
| `apps/NPXS40008/application.ps.bundle` | **rnps-settings**, the Settings app | E1 `CN=rnps-settings`; E2; E3 job `s-settings_v2_...`, roots `src/components/{SettingsList,TabbedList,TabbedPanelBase}`, `src/modules/{network,devices,accessibility,dateTime,language,notifications,powerSave,savedData,initialSetup,gamesApps,screenVideoPlayback}`; E4 168 asset files; E5 two debug-settings tiles | Yes, full screen |
| `apps/NPXS40009/application.ps.bundle` | **rnps-millenniumfalcon**, the user, account and initial-setup wizard, i.e. the profile flow | E1 `CN=rnps-millenniumfalcon`; E2 `millennium-falcon`; E3 job `niumfalcon_v2_...`, 150 app-local roots including `src/modules/initialSetup` and about 70 `src/modules/pf*` screens (`pfLogin`, `pfEula`, `pfCoppa`, `pfChildAccountCreation`, `pfParentalControl`, `pfTwoSVSetup`, `pfCreatePasscode`, `pfFamilyManagement`, `pfChangeOnlineId`); `psinitialsetup:complete` | Yes, full screen |
| `apps/NPXS40081/application.ps.bundle` | **rnps-millenniumfalcon-dialog**, the same profile-flow code shipped as a modal surface | E1 `CN=rnps-millenniumfalcon-dialog`; E3 the same 150 app-local source roots as NPXS40009; E4 the two asset trees are identical, 222 files with matching names and sizes; plaintext lengths differ by 0.08% | Yes, dialog |
| `apps/NPXS40013/application.ps.bundle` | **rnps-profile**, the player profile: overview, friends, games, posts, background editor | E1 `CN=rnps-profile`; E2; E3 job `ps-profile_v2_...`, roots `src/screens/{OverviewScreen,FriendsScreen,FriendsGridScreen,GamesScreen,PostsScreen,PostDetailScreen,EditBackgroundScreen,PlayerReviewScreen,TabScreen}` | Yes, full screen |
| `apps/NPXS40015/application.ps.bundle` | **rnps-search**, the shell search screen | E1 `CN=rnps-search`; E2; E3 job `nps-search_v2_...`, single app-local root `packages/search-ui/src` | Yes, full screen |
| `apps/NPXS40016/application.ps.bundle` | **monte-carlo**, the media hub behind the TV & Video, Music and All Apps tiles | E1 `CN=monte-carlo`; E2; E3 job `onte-carlo_v2_...`, roots `src/application/monteCarlo.js`, `src/components/{MediaStoreBody,MediaOOBE,ContentHubPreview,ContentHubCTA,ServiceHubCTA,VideoPlayer,HubPreview}`; E5 hosts tiles NPXS40053 "TV & Video" `psmediahub:main?id=video_home`, NPXS40054 "Music Home" `?id=music_home`, NPXS40056 "All Apps" `?id=all_apps` | Yes, full screen |
| `apps/NPXS40033/application.ps.bundle` | **rnps-game-hub**, the per-game page under the Home row | E1 `CN=rnps-game-hub`; E2; E3 job `s-game-hub_v2_...` x384, root `packages/game-hub/src`; deep link `psgamehub:main`; E4 `assets/src/screens/PostPurchaseScreen` | Yes, full screen |
| `apps/NPXS40047/application.ps.bundle` | **elysion**, the PlayStation Store | E1 `CN=elysion`; E2; E3 job `elysion_v2_...` plus 233 self-name tokens, roots `src/components/{store,storefront-country,wallet-balance,wishlist,picker,hub-scenes,hub-templates,settings-menu}`, dependencies `@rnps-ppr/curiosity` (ad tiles) and `@rnps-ppr/pricing`; E5 tile "PlayStation Store" `pshome:gamestore` | Yes, full screen |
| `apps/NPXS40063/application.ps.bundle` | **rnps-explore-hub**, the Explore hub: news, UGC, ads | E1 `CN=rnps-explore-hub`; E2; E3 job `xplore-hub_v2_...`, roots `src/routes/{Home,Details,Settings}`, `src/scenes/{News,UGC}`, `src/modules/ads`, `src/containers/{Hub,Ads,Navigator}`; E5 tile "Explore" `pshome:explorehub` | Yes, full screen |
| `apps/NPXS40071/application.ps.bundle` | **rnps-library**, the Game Library and App Library | E1 `CN=rnps-library`; E2; E3 job `ps-library_v2_...`, roots `src/components/{InstalledScreen,InstalledMediaScreen,MyCollectionScreen,MyCollectionMediaScreen,PSNowScreen,PSPlusScreen,LibraryGridItem,ExperienceScreen}`; E5 tiles "Game Library" `pslibraryhub:main?space=game` and NPXS40139 "App Library" `?space=media` | Yes, full screen |
| `apps/NPXS40062/main.jsbundle` | **rnps-universal-checkout**, the cart, payment-method and purchase flow | E1 `CN=rnps-universal-checkout`; E2 `universal-checkout`; E4 86 asset files under `modules/{arcrunner-cart,arcrunner-payment,arcrunner-ui,uc-widget}`, including `redeem-codes.png`, `add-paypal-qrcode.png`, `credit-cards/{AMEX,DISCOVER,CB,CARTEBLEUE}/{LARGE,SMALL}.png` and `apms/{PAYPAL,IDEAL,ALIPAY,GIROPAY,BANCONTACT,KAKAOPAY,SOFORT,BOKU,PAYSAFE,POSTEPAY,YANDEX,CASHU}.png` | Yes, full screen |
| `apps/NPXS40027/application.ps.bundle` | **rnps-igc-browse**, in-game-commerce category browse and the product detail page | E1 `CN=rnps-igc-browse`; E2 `igc-browse`; E3 job `igc-browse_v2_...` plus 124 self-name tokens, roots `src/modules/{category,category-root,preview}`, `src/containers/pdp.js`, `src/components/category-tile.js`, dependency `@rnps-ppr/game-cta` (sku selector, button group, countdown) | Yes, full screen |
| `apps/NPXS40041/application.ps.bundle` | **rnps-titlestore-preview**, a preview or debug build of the same code as NPXS40027 | E1 `CN=rnps-titlestore-preview`, a distinct CN, so not an accidental duplicate; E3 an app-local source-root set identical to NPXS40027; the 3.00 JavaScript for the two is the same length, 4,119,928 bytes, and differs in only 2,276 bytes (0.055%); the 4.03 plaintext lengths are both exactly 2,304,990; E5 tile "Store Preview" (starred, internal) | Yes, debug only |
| `apps/NPXS40018/application.ps.bundle` | **rnps-gaming-lounge**, Game Base: messages, parties, friends | E1 `CN=rnps-gaming-lounge`; E2 `gaming-lounge`; E3 job `ing-lounge_v2_...`, `apps/gaming-lounge` x92, roots `src/modules/{chats,friends}`, `src/components/{MessageList,StickerPanel,VoiceMessage,ScreenSharePanel,AttachedImagePanel,UrlPreviewPanel,CreateNewGroupTile,NotifyNewMessage}`; `psgl:` deep links x16 in the Control Center bundle | Yes, full screen |
| `apps/NPXS40032/application.ps.bundle` | **rnps-service-hub-psnow**, the PlayStation Now service hub (codename Artemis) | E1 `CN=rnps-service-hub-psnow`; E2; E3 job `rnps-psnow_v2_...`, roots `src/components/{subscription-details,bandwidth-test,hub-previews,predefined-strands,video-background,scene-list}`, `src/modules/age-gating`; E6 `appName: "artemis-psnow"`; E4 `assets/applications/psnow/appdb`; E5 tile "PlayStation Now" `psnow:browse` | Yes, full screen |
| `apps/NPXS40037/application.ps.bundle` | **rnps-service-hub-psplus**, the PlayStation Plus service hub, same code base as NPXS40032 | E1 `CN=rnps-service-hub-psplus`; E2; E3 job `nps-psplus_v2_...` with a source-root set identical to NPXS40032; plaintext lengths 2,842,390 vs 2,841,888; E4 `assets/applications/psplus/appdb`; E5 tile "PlayStation Plus" `psplus:browse` | Yes, full screen |
| `apps/NPXS40021/application.ps.bundle` | **rnps-system-modal-dialog**, the system modal dialog host | E1 `CN=rnps-system-modal-dialog`; E2 `reactSystemModalDialog`; E3 job `dal-dialog_v2_...`, roots `src/modules/{SystemUpdaterDialog,SystemUpdaterRequestDialog,PowerOffWarningDialog,CrashReportDialog,DbRecoveryDialog,SelectResolutionDialog,StorageProgressDialog,InstallOptionDialog,PsvrUpdaterDialog,AutoMounterErrorDialog,PreorderDetailDialog,MediaEventListenerDialog}` | Yes, dialog |
| `apps/NPXS40011/main.jsbundle` | **rnps-notification-overlay**, toasts, the multi-login popup, and the VR play-area and screen-lock indicators | E1 `CN=rnps-notification-overlay`; E2; E4 `assets/packages/notification-view-template/`, `assets/src/components/Popup/assets/{Notification_MultiLogin_base.png,morpheus_notification_base_dh118_dv118_mh102_mv102.dds}`, `assets/src/components/VrIndicator/{OutOfPlayAreaWarning,ScreenLockIndicator}/assets/*` | Yes, overlay |
| `apps/NPXS40064/application.ps.bundle` | **rnps-x-wing**, the purchases and subscriptions account screens | E1 `CN=rnps-x-wing`; E2; E3 job `nps-x-wing_v2_...` with module namespaces `@rnps-ppr/x-wing/{transaction-history,purchase-settings,playstation-subscriptions,initializer}` and `@rnps-ppr/x-wing-core/error-handler` | Yes, full screen |
| `apps/NPXS40080/application.ps.bundle` | **rnps-ppr-shareplay**, Share Play, dialog plus hub | E1 `CN=rnps-ppr-shareplay`; E2 `rnps-share-play`; E3 job `share-play_v2_...`, `apps/share-play`, roots `src/SharePlayDialog/`, `src/SharePlayHub/` | Yes, both |
| `apps/NPXS40161/application.ps.bundle` | **rnps-screen-share**, the screen-share viewer | E1 `CN=rnps-screen-share`; E2; E3 job `reen-share_v2_...`, `apps/screen-share`, roots `src/{components,containers}/{FullStreamView,WatchInfo,MultitaskMenu}`, `src/components/{StreamDetail,OverlayPanel}` | Yes, overlay |
| `apps/NPXS40138/application.ps.bundle` | **npparty-compatibility-app**, joining and creating legacy NP parties | E1 `CN=npparty-compatibility-app`; E2; E3 job `bility-app_v2_...`, roots `src/containers/{Join,Creating,CreateResult,Inviting,Leaving,LeaveConfirmation}`, `src/providers/NpPartyProvider.js`, `src/navigators/PlayerSelectionScreen` | Yes, full screen |
| `apps/NPXS40154/application.ps.bundle` | **rnps-remoteplay-hub**, the Remote Play hub tile page | E1 `CN=rnps-remoteplay-hub`; E2; E3 job `rnps-remoteplay-hub_v2_master`, roots `src/components/{HubWrapper,PreviewContent,MainButton,RightPosterComponent,LayoutStatus}`; 381 KB plaintext, the smallest full-screen app | Yes, full screen |
| `apps/NPXS40163/application.ps.bundle` | **rnps-onboard-download**, the first-boot download progress screen | E1 `CN=rnps-onboard-download`; E2; E3 job `d-download_v2_...`, roots `src/components/{progress-bar,tile}`, `src/modules/{main,error}` | Yes, full screen |
| `apps/NPXS40046/application.ps.bundle` | **rnps-profile-dialog**, the small profile picker dialog | E1 `CN=rnps-profile-dialog`; E2; E3 job `ile-dialog_v2_...`, only `src/application/index.js` and `src/utils/with-common-dialog`; 288 KB plaintext, the smallest signed app bundle | Yes, dialog |
| `apps/NPXS40043/main.jsbundle` | **rnps-psnow-player**, the cloud-streaming player and its loading tips | E1 `CN=rnps-psnow-player`; E2; E4 `assets/src/assets/images/tips/*` with `Cloud-Storage.png`, `Low-Connection-Quality.png`, `Play-On-PC_4K.png`, `Continue-Your-Game-Anywhere.png`, `Max-Resolution.png`, `Sync-Your-Trophies.png`, `TV-Game-Mode.png` | Yes, full screen |
| `apps/NPXS40075/main.jsbundle` | **rnps-media-gallery**, the Media Gallery | E1 `CN=rnps-media-gallery`; E2; E4 `assets/src/assets/texture/{MediaGallery-Graphic-2x.png,lineArt_static_PXG02_cloudMediaGallery_onboarding.png}`; E5 tile "Media Gallery" `psmediagalleryhub:main` | Yes, full screen |
| `apps/NPXS40144/main.jsbundle` | **rnps-unsupported-title-hub**, the hub shown for discs the console cannot run | E1 `CN=rnps-unsupported-title-hub`; E2; E5 four tiles, "Unsupported Disc", "Unreadable Disc", "PlayStation 5 Format Disc", "PlayStation 4 Format Disc", all `unsupported:main?type=...` | Yes, full screen |
| `apps/NPXS40145/main.jsbundle` | **rnps-compilation-disc-hub**, the hub for compilation discs | E1 `CN=rnps-compilation-disc-hub`; E2; E5 tile "Compilation" `compilation:main` | Yes, full screen |
| `apps/NPXS40025/main.jsbundle` | **rnps-trophy**, the trophy list and trophy detail | E1 `CN=rnps-trophy`; E2; E4 `assets/src/assets/icon/{unacquired_trophy.png,Trophy_Privacy.png}` | Yes, full screen |
| `apps/NPXS40066/main.jsbundle` | **rnps-discplayer**, the disc and Blu-ray player control panel | E1 `CN=rnps-discplayer`; E2 `rnps-disc-player`; E4 `assets/src/assets/{ControlPanelMain,DVDRegion,JumpPanel}/*` | Yes, full screen |
| `apps/NPXS40110/main.jsbundle` | **rnps-discplayer-hub**, the disc-player hub tile page | E1 `CN=rnps-discplayer-hub`; E2 `rnps-disc-player-hub`; E4 `assets/src/assets/HubPreview/Thumbnail_Film_Base.png` | Yes, full screen |
| `apps/NPXS40097/main.jsbundle` | **rnps-game-hub-preview-launcher**, an internal launcher for game-hub previews | E1 `CN=rnps-game-hub-preview-launcher`; E2; E5 tile "Game Hub Preview App" (starred, internal) | Yes, debug only |
| `bgs/NPXS40052/main.jsbundle` | **ppr-bgs**, the shell background service | E1 `CN=ppr-bgs`; E2 its manifest carries `updateType: "bgsservice"`, the only manifest that does; it is the only entry under `bgs/` rather than `apps/`; no `appdb`, no `assets`; the Home bundle talks to it over `rnps-to-bgs-push` and `rnps-bgs-rpc` | No, service |
| `apps/NPXS40141/base_dll.ps.bundle` and `host.ps.bundle` | **js-launcher** (manifest codename `apennine`), the shared React Native runtime base plus a 2.3 KB host shim. **This is the `base_dll` every other bundle preloads** | E1 both bundles carry `CN=js-launcher`; E2 `applicationName: "apennine"` 0.1.0+36; the base bundle is literally named `base_dll.ps.bundle`; E3 its only embedded paths are `E:\jenkins_slave\workspace\rnps__ppr_releases_03.00\apennine\node_modules\react-native{,-playstation}\...`, with zero app-local `src/` roots, so it ships framework code and no screens, and it is the only Windows-built job in the corpus | No, framework |

### Medium confidence

Identity certain from E1; the screen is inferred from the name plus one weak
corroborating signal.

| Bundle file | App / screen it implements | Evidence | Surface |
| --- | --- | --- | --- |
| `apps/NPXS40007/main.jsbundle` | **rnps-uam-fs**, the full-screen Activities view | E1 `CN=rnps-uam-fs`; E2; UAM semantics come from the decrypted Control Center and action-cards bundles, where `UAM_TYPE = {PROGRESS, OPEN_ENDED, COMPETITIVE, CHALLENGE, MULTIPLAY_PROGRESS, MULTIPLAY_OPEN_ENDED, MULTIPLAY_COMPETITIVE}` and the card ids `UAM_RUNNING_GAME = "uam:running"` and `UAM_CROSS_GAME` appear in the default and resume card orders. Those are exactly the PS5 Activity card kinds. Reading `-fs` as "full screen" is inference, and the app ships a single asset, `src/assets/icons/black.png` | Yes, full screen |
| `apps/NPXS40108/main.jsbundle` | **rnps-player-review**, the player review and commendation flow | E1 `CN=rnps-player-review`; E2; no assets and no decrypted JavaScript. Corroborated only indirectly: `rnps-profile` ships a `src/screens/PlayerReviewScreen`, and the Control Center bundle opens `pspr:show?accountId=<id>` and `pspr:browse` | Yes, likely dialog |
| `apps/NPXS40004/main.jsbundle` | **rnps-player-selection-dialog**, the "which player" picker | E1, E2; the NP party app ships an equivalent `src/navigators/PlayerSelectionScreen`, which shows the pattern exists but says nothing about this bundle's contents | Yes, dialog |
| `apps/NPXS40006/main.jsbundle` | **rnps-invitation-dialog**, game and party invitation prompt | E1, E2; name only, with invitation flows visible in the gaming-lounge and NP party sources | Yes, dialog |
| `apps/NPXS40017/main.jsbundle` | **rnps-content-information**, the content information sheet | E1, E2; the Control Center bundle carries a `pscontentinfo:` deep link | Yes, dialog |
| `apps/NPXS40024/main.jsbundle` | **rnps-capture-menu**, the capture and share menu | E1, E2; the Control Center bundle carries a `pscapturemenu:` deep link | Yes, overlay |
| `apps/NPXS40029/main.jsbundle` | **rnps-playgo-dialog**, play-as-you-download progress prompts | E1, E2; name only | Yes, dialog |
| `apps/NPXS40031/main.jsbundle` | **rnps-bgft**, background file transfer UI | E1, E2; `bgft` appears in every decrypted bundle's shared URI table, 3 to 26 hits, which shows it is a system-wide service surface but not what this bundle draws | Yes, likely dialog |
| `apps/NPXS40034/main.jsbundle` | **rnps-message-dialog**, generic message dialog | E1, E2; name only | Yes, dialog |
| `apps/NPXS40035/main.jsbundle` | **rnps-savedata-dialog**, saved-data upload and download dialog | E1, E2; its asset tree holds only React Navigation back-icons, so it carries no art of its own | Yes, dialog |
| `apps/NPXS40040/main.jsbundle` | **rnps-app-installer**, application install progress | E1, E2; name only; 1.1 MB, consistent with a single-purpose dialog | Yes, dialog |
| `apps/NPXS40044/main.jsbundle` | **rnps-broadcast**, broadcast setup | E1, E2; the action-cards host also ships a `packages/broadcast/src`, so broadcast UI exists in two places and the split is unresolved | Yes, likely dialog |
| `apps/NPXS40048/main.jsbundle` | **rnps-agent-popupgui**, the voice-assistant popup | E1, E2; the Control Center bundle carries a `psvoiceagent:` deep link and the Settings bundle mentions `voiceagent` 126 times | Yes, overlay |
| `apps/NPXS40051/main.jsbundle` | **rnps-web-launcher**, launcher shim for the web browser | E1, E2; the action-cards host owns the actual `packages/webbrowser/src`, so this is likely a thin entry point; 1.09 MB | Yes, likely launcher |
| `apps/NPXS40070/main.jsbundle` | **rnps-system-message-client**, system message display | E1, E2; the Control Center bundle carries a `pssystemmessage:` deep link | Yes, dialog |
| `apps/NPXS40089/main.jsbundle` | **rnps-cdlg-usbstorage**, the USB storage dialog | E1 `CN=rnps-cdlg-usbstorage`; E2 `usbstoragedialog`; the `cdlg` (common dialog) prefix appears in every decrypted bundle's shared table | Yes, dialog |
| `apps/NPXS40103/main.jsbundle` | **rnps-netctlap-dialog**, network access-point setup dialog | E1, E2; name only | Yes, dialog |
| `apps/NPXS40147/main.jsbundle` | **rnps-legal-docs**, the legal document viewer | E1, E2; the profile-flow app has a matching `src/modules/pfLegalDocs`, a different implementation of the same subject | Yes, full screen |
| `apps/NPXS40167/application.ps.bundle` | **rnps-wishlist**, the store wishlist | E1, E2; the Store app ships its own `src/components/wishlist`, so the division between the two is unresolved | Yes, full screen |
| `apps/NPXS40182/main.jsbundle` | **rnps-vr-onboarding**, PS VR2 onboarding | E1 `CN=rnps-vr-onboarding`; E2 spells it `rnps-vr-onbarding`, the certificate has it right; E4 one asset, `src/assets/texture/MuteButton.png` | Yes, full screen |
| `apps/NPXS40005/main.jsbundle` | **rnps-game-custom-data-dialog**, the game-supplied custom data prompt | E1, E2; name only | Yes, dialog |

### Low confidence

Identity certain, role not established. Codenames or abbreviations with no
corroborating source path, asset or tile anywhere in the corpus.

| Bundle file | App / screen it implements | Evidence | Surface |
| --- | --- | --- | --- |
| `apps/NPXS40072/main.jsbundle` | **rnps-cosmiccube**, unknown | E1 `CN=rnps-cosmiccube`; E2; the only app-specific asset is `src/assets/texture/UXD-10383.png`, a design-ticket filename that identifies nothing | Unknown |
| `apps/NPXS40026/main.jsbundle` | **rnps-lfps-bc**, plausibly a "Live from PlayStation" back-compat surface, unproven | E1, E2; `lfps` appears 2 to 4 times in the profile, game-hub, gaming-lounge and explore bundles, always inside the shared URI table | Unknown |
| `apps/NPXS40107/main.jsbundle` | **rnps-g2p-dialog**, unknown, `g2p` unexpanded | E1, E2; `g2p` appears exactly twice in each of 14 decrypted bundles, which is the shared table again and carries no meaning | Likely dialog |

## Cross-reference: the 19 named 4.02 packages

Every package in `games/rnps_4.02` maps to exactly one 4.03 NPXS id, and the
mapping is made by the certificate CN, not by the file name. Four file names
disagree with what is actually inside them, and one of those disagreements
matters: `igc-browse.epkg` would otherwise be ambiguous between NPXS40027 and
NPXS40041, which ship nearly the same code under two different names.

| 4.02 package file | Internal name at header `0x74` | Leaf certificate CN | Package version | Maps to | Basis |
| --- | --- | --- | --- | --- | --- |
| `rnps-home.epkg` | `rnps-home` | `CN=rnps-home` | 4.2.0+13450 | NPXS40002 | CN match, exact |
| `rnps-control-center.epkg` | `rnps-control-center` | `CN=rnps-control-center` | 4.2.0+45353 | NPXS40003 | CN match, exact |
| `rnps-action-cards.epkg` | `rnps-action-cards-host-app` | `CN=rnps-action-cards-host-app` | 4.2.0+45353 | NPXS40036 | CN match; the file name is short for the internal name. Identical build number to the Control Center package, confirming the shared monorepo seen in the 3.00 build paths |
| `rnps-settings.epkg` | `rnps-settings` | `CN=rnps-settings` | 4.5.1+22993 | NPXS40008 | CN match, exact |
| `rnps-game-hub.epkg` | `rnps-game-hub` | `CN=rnps-game-hub` | 4.16.0+18533 | NPXS40033 | CN match, exact |
| `rnps-library.epkg` | `rnps-library` | `CN=rnps-library` | 4.1.0+12430 | NPXS40071 | CN match, exact |
| `rnps-explore-hub.epkg` | `rnps-explore-hub` | `CN=rnps-explore-hub` | 4.2.0+5566 | NPXS40063 | CN match, exact |
| `rnps-notification-overlay.epkg` | `rnps-notification-overlay` | `CN=rnps-notification-overlay` | 4.1.0+9936 | NPXS40011 | CN match, exact |
| `rnps-profile.epkg` | `rnps-profile` | `CN=rnps-profile` | 4.1.1+11793 | NPXS40013 | CN match, exact |
| `rnps-search.epkg` | `rnps-search` | `CN=rnps-search` | 4.1.0+9093 | NPXS40015 | CN match, exact |
| `rnps-player-review.epkg` | `rnps-player-review` | `CN=rnps-player-review` | 4.0.1+2478 | NPXS40108 | CN match, exact |
| `rnps-uam-fs.epkg` | `rnps-uam-fs` | `CN=rnps-uam-fs` | 4.0.0+13765 | NPXS40007 | CN match, exact |
| `millennium-falcon.epkg` | `rnps-millenniumfalcon` | `CN=rnps-millenniumfalcon` | 4.1.0+22649 | NPXS40009 | CN match; the file name uses the hyphenated codename, the certificate does not |
| `rnps-millenniumfalcon-dialog.epkg` | `rnps-millenniumfalcon-dialog` | `CN=rnps-millenniumfalcon-dialog` | 4.0.1+498 | NPXS40081 | CN match, exact. Present in the folder but absent from the 18-name component list |
| `monte-carlo.epkg` | `monte-carlo` | `CN=monte-carlo` | 4.0.6+15454 | NPXS40016 | CN match, exact |
| `elysion.epkg` | `elysion` | `CN=elysion` | 4.2.0+7193 | NPXS40047 | CN match, exact |
| `igc-browse.epkg` | `rnps-igc-browse` | `CN=rnps-igc-browse` | 4.0.1+4614 | NPXS40027, not NPXS40041 | The CN disambiguates. NPXS40041 signs as `CN=rnps-titlestore-preview` and is otherwise near-identical code |
| `universal-checkout.epkg` | `rnps-universal-checkout` | `CN=rnps-universal-checkout` | 4.3.0+20858 | NPXS40062 | CN match; the file name drops the `rnps-` prefix |
| `ppr-bgs.epkg` | `ppr-bgs` | `CN=ppr-bgs` | 4.5.0+16692 | NPXS40052, under `bgs/` not `apps/` | CN match, exact |

Two further checks on this set:

- `rnps-home.epkg` is byte-identical to the separately extracted 4.2 OTA package
  in `games/useful rnps/ota_package`, SHA-256
  `F379999924FAD51C623CA799E9E5A954F578F080CA30246A524E1731FE1B0CAE`, and its
  header version words decode to 4.2.0+13450, exactly the `applicationVersion` in
  the OTA manifest beside the extracted bundle. The header version fields are
  therefore confirmed against an independent plaintext source.
- Each package's `RNPSHEDR` starts at file offset 640 and its first 32 bytes match
  the corresponding firmware bundle's header prefix before diverging, so these are
  the same container type at a different build, not a different format.

Three entries in the named component list describe code shared with another
entry, which is worth stating plainly rather than leaving to be rediscovered:
`rnps-action-cards` and `rnps-control-center` come out of one build job,
`millennium-falcon` and `rnps-millenniumfalcon-dialog` are one application in two
surfaces, and `igc-browse` has a twin in the firmware under a different
certificate.

## Corrections to the previous version of this document

The earlier map was built from `appName` literals and keyword density (E6) alone.
Those literals are genuine, but reading a feature out of a codename produced five
wrong entries, corrected above:

| Id | Previous claim | Correct | What settled it |
| --- | --- | --- | --- |
| `NPXS40016` | accessibility / screen-reader companion | media hub for TV & Video, Music, All Apps | E5 tiles NPXS40053/40054/40056 with `psmediahub:` URIs, plus E3 `src/components/{MediaStoreBody,MediaOOBE,VideoPlayer}` |
| `NPXS40047` | PS4-compatibility / legacy shell surface | the PlayStation Store | E5 tile "PlayStation Store" `pshome:gamestore`, plus E3 `src/components/{store,storefront-country,wallet-balance}` |
| `NPXS40009` | media / player framework app | the account and initial-setup profile flow | E3, about 70 `src/modules/pf*` screens plus `src/modules/initialSetup` |
| `NPXS40064` | small utility shell app | purchases and subscriptions | E3 `@rnps-ppr/x-wing/{transaction-history,purchase-settings,playstation-subscriptions}` |
| `NPXS40141` | Keyboard / IME | `js-launcher` / `apennine`, the shared `base_dll` RN runtime | E1 `CN=js-launcher`, the file is named `base_dll.ps.bundle`, and its only paths are `react-native` and `react-native-playstation` Libraries. The `Keyboard` hits are React Native's own `Keyboard.js`, not an IME |

One structural claim also needs retracting. The previous version said the shared
`base_dll` bundle "is not in `readable_js_3.00` and not in `decrypted_bins_3.00`",
and therefore that `FontSizePS`, `FocusLayerPS`, `OptionsMenuPS`, `MenuListItemPS`
and the named easing curves were unresolvable from JavaScript. That is wrong.
`base_dll` is NPXS40141, it is present in both sets as `NPXS40141.base.bin` and
`NPXS40141.base.js`, and a direct search of that file finds `FontSizePS` x11,
`FocusLayerPS` x6, `MenuListItemPS` x10, `OptionsMenuPS` x2 and `easeOutBreezePS`
x4, including the UI2-to-UI3 migration warning
`"Using UI2 FontSize on UI3: ... Please use FontSizePS."`. The shared component
library is readable; it simply was not being read. `SettingsListPS` is genuinely
absent from it and does belong to the native or PUI side.

## What is still unidentified, and what would settle it

**Roles that name evidence alone cannot decide.** `rnps-cosmiccube` (NPXS40072),
`rnps-lfps-bc` (NPXS40026) and `rnps-g2p-dialog` (NPXS40107) have certain
identities and no recoverable role: absent from the 3.00 decryption set, no
distinguishing art, no shell tile, and their only hits elsewhere are in the shared
URI constant table that every bundle carries. `rnps-uam-fs` (NPXS40007) and
`rnps-player-review` (NPXS40108) are one step better, in that their subject matter
is recoverable from other apps, but nothing shows what these two bundles draw.

**What would settle them, cheapest first.**

1. *Read the deep-link registry instead of the bundles.* Each app is reached by a
   URI scheme (`pscontrolcenter:`, `psgamehub:`, `pslibraryhub:`, `pspr:`,
   `pscheckout:`, `psvoiceagent:`, `pscontentinfo:`, `pssystemmessage:`). The
   scheme-to-title-id table lives on the native side in the `Sce.Vsh.*` launcher
   assemblies, and `tools/shell-metadata` already reads that managed metadata
   without executing anything. If that table names title ids it identifies
   NPXS40026, NPXS40072 and NPXS40107 directly, independent of any decryption, and
   it also resolves the `rnps-wishlist` versus store-internal wishlist and
   `rnps-broadcast` versus action-card-broadcast splits. This runs on the host.
2. *Extend the 3.00 decryption set.* The corpus decrypts 28 of 63 bundles, and
   every high-confidence row above rests on one of those 28 or on an `assets/` or
   `appdb` tree. The payload in `games/useful rnps/decrypt_tool_all_bundles` is
   already configured to dump those 28; pointing it at NPXS40007, NPXS40011,
   NPXS40026, NPXS40062, NPXS40072, NPXS40107 and NPXS40108 would convert every
   remaining medium and low row to high in one pass. This needs a compatible PS5,
   not a host tool.
3. *Mine the nine `.rco` archives in `vsh_asset`.* If the packed PUI resource
   archives carry per-title strings they would supply user-facing labels for apps
   that register no `appdb` tile. This needs an RCO unpacker, which does not exist
   yet.

**Not worth pursuing.** Recovering the 4.02 package bodies by attacking the
container. The pair corpus already shows a 16-byte-block cipher with no reused
keystream, and a sweep of 234,852 byte-aligned AES-128/192/256 key windows across
every pre-body header found zero raw key material, so the content key is not
sitting in the header in plain form. The 400-byte block at `0x40` is large enough
for an IV plus an RSA-3072 wrapped key, but proving that needs the kernel parser.
Meanwhile the certificate CN already answers the identity question those bodies
would confirm, which is exactly why identity is settled here and role is not.
