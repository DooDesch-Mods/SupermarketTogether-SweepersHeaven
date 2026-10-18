# Changelog

## [1.4.0] - 2026-10-18

- Move broom collection, throws and product motion to host authority with client intents.
- Share host settings and restore existing product positions for joining players.
- Reject repeated or excessive requests and validate the requesting player, equipment and range.
- Apply the item limit to the entire sweep and guard native recovery against duplicate refunds.
- Replace recursive trash collection with a bounded queue and clear session state on disconnect.
- Support the current Mini Transporter component as well as the older vehicle path.
- Remove the stock-consuming debug spawner from the player plugin.
- Preserve existing gameplay settings and add a configurable host cooldown and master switch.

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
