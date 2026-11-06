---
name: localization
description: Use when changing FClash native UI text or shared JSON translation catalogs.
---
# Native localization
Catalogs: `resources/translations/intl_{en,zh_CN,ja,ru}.json`. Reuse entries; add source translations in all four catalogs for new product text. Windows UiStrings and Kotlin native lookup load JSON directly. Do not edit generated build copies. Verify lookup/locale switching and compile affected clients. Legacy preference names are upgrade compatibility, not visible product labels.
