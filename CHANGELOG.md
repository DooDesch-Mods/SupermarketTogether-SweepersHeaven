# Changelog

## [1.4.0] - 2026-10-18

### Added

- Synchronized broom sweeps, throws and dropped product movement in co-op. The host handles collection and refunds.
- Shared host settings, a configurable sweep cooldown and an enable/disable setting.

### Fixed

- Joining players receive the current positions of dropped products.
- Repeated pickup requests cannot collect the same product or pay its refund twice.
- The item limit applies to the entire sweep.
- Mini Transporter pickup supports the current game version.

### Changed

- Optional bulk trash pickup clears remaining trash in small batches.
- Removed the old debug product spawner because it could consume store stock.

### Compatibility

- Install this version on the host and each player who wants synchronized sweeps and product movement.

## v1.3.1 - Fix error when picking up a single item in the store

## v1.3.0 - Pickup all trash at once Feature

- Added feature to pickup all trash at once, it's disabled by default, you can enable it in the config file

## v1.2.1 - Fix collecting products when hitting thiefs

## v1.2.0 - Mini Transporter product pickup

- Added support for picking up items using the Mini Transporter

## v1.1.0 - Rightclick throw items

- Added rightclick to throw items around withouth picking them up

## v1.0.1 - Update Readme

## v1.0.0 - Initial Release

### Features

- **Mass Item Collection**: Enables players to sweep up multiple items at once.
- **Configurable Options**:
  - Adjustable pickup radius and maximum items per sweep.
  - Customizable pickup and spawn keys.
- **Debug Mode**: Adds special options for testing item spawning.
- **Physical Feedback**: Items are physically affected when collected.

### Notes

- Debug mode is optional and can be toggled in the configuration.
- Includes compatibility with BepInEx 5.
