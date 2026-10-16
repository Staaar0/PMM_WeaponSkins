# PMM_WeaponSkins

Panorama menu for the [WeaponSkins](https://github.com/Staaar0/WeaponSkins) plugin.

<img width="512" height="288" alt="Image" src="https://github.com/user-attachments/assets/2d31835f-96cd-4339-804f-5772b7ea12b0" />

## Requirements

- CounterStrikeSharp 1.0.376 or newer
- WeaponSkins `"Menu_Style": 3`
- [PanoramaMenuManagerCS2](https://github.com/RRimmer/PanoramaMenuManagerCS2)
- [MultiAddonManager](https://github.com/Source2ZE/MultiAddonManager)

## Install

1. Download `PMM_WeaponSkins.zip` from the [latest release](../../releases/latest).
2. Copy `Server-plugins/counterstrikesharp` into `game/csgo/addons/`.

## Workshop

1. Use files inside `Content-addonmanager/panorama` if you want your own Workshop addon.
2. Ready to use Workshop addon [3821029634](https://steamcommunity.com/sharedfiles/filedetails/?id=3821029634)
3. Put the Workshop addon's id into `mm_client_extra_addons`.
4. Restart the server.

When a new release changes the files in `Content-addonmanager`, publish the Workshop addon again (a new Workshop item is the safest) and use its new id.

## Config

path `configs/plugins/PMM_WeaponSkins/PMM_WeaponSkins.json`

```json
{
  "Language": "en",
  "OpenDelay": 0.2,
  "InputDelay": 0.2,
  "CardsPerPage": 40,
  "PageResetDelay": 0.1,
  "ConfigVersion": 1
}
```
