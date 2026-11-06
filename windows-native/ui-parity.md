# Flutter → WinUI presentation contract

The Windows migration replaces the rendering framework. It must preserve the product's existing visual hierarchy and interaction model. Use the Flutter source as the reference, not the first WinUI preview's layouts.

| Area | Existing behavior to preserve | Source |
|---|---|---|
| Navigation | 80 logical pixel rail; centered 50 px app badge; Material icons; menu toggles labels below icons. Resources stays under Tools; logs are optional; proxies appear when available. | `lib/manager/app_manager.dart`, `lib/common/navigation.dart` |
| Dashboard | 16 px padding, 14 px gaps, 14 px card corners; shortest-column placement with `max(4 * ceil(contentWidth / 280), 8)` units. Speed spans 8 units; other default cards span 4. Short cards are 80 px; tall cards are 174 px. | `lib/views/dashboard/dashboard.dart`, `lib/widgets/grid.dart`, `lib/common/constant.dart` |
| Default cards | Speed, system proxy, TUN, outbound mode, network detection, traffic, local IP. Never automatically add memory or a current-profile card. Preserve saved ordering/customization. | `lib/models/config.dart` |
| Dashboard contents | Speed curve with separate up/down text, donut usage chart, radio outbound modes, one active local IPv4 address. Card click opens options; switches change the value directly. Edit/add/save remain in the page toolbar. | `lib/views/dashboard/widgets/` |
| Primary actions | Start/stop at dashboard bottom right, delay test at proxies bottom right, add profile at profiles bottom right. | dashboard `start_button.dart`, `proxies.dart`, `profiles.dart` |
| Tools | More → Resources/Logs; Settings → language/theme/backup/hotkeys/basic/advanced/application; Other → About. DNS, additional rules, network and scripts stay under Advanced. | `lib/views/tools.dart`, `lib/views/config/advanced.dart` |
| DNS | Compact settings rows; click a server/policy entry to open its ordered list, then edit a row. Add/edit actions belong in the toolbar. | `lib/views/config/dns.dart` |
| Profiles | Radio selection and card click, ellipsis context menu for edit/update/overrides/copy/export/delete. Update-all and reorder in the toolbar; add in the floating action. | `lib/views/profiles/profiles.dart` |
| Proxies | Group tabs, search and options in the toolbar, responsive 14 px rounded cards with cached flags. Selected cards use the theme's secondary container and a 1 px primary outline. URL-test/fallback selections can return to automatic mode. | `lib/views/proxies/` |
| About | Logo, FClash, version, description and check updates. No framework implementation label or removed external links/contributors. | `lib/views/about.dart` |

`FClash.Domain/LegacyUiLayout.cs` ports the Flutter grid contract. Its tests assert the default composition, exact small-window packing, responsive bounds and absence of overlap. `LegacyDashboardPanel` applies it to WinUI, including in-place edit/reorder.

Material icons and Roboto retain their bundled licenses. `Assets/material-palettes.json` preserves the original Material You palette as native data; no framework SDK is required to read it. The `lib/...` references above identify the historical 0.8.96 interface in Git history; those runtime sources have been removed after native migration. Layout sizes/order, navigation and interaction contracts remain applicable to both native clients.

Verify native presentation only with isolated safe-mode data; never start Core or alter the developer's network to inspect UI. Use logical pixels and account for monitor DPI when comparing screenshots. Native font rasterization, focus cues and popup animations can differ; such differences do not justify reorganizing pages or controls.
