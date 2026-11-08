using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem.LowLevel;
using YARG.Core.Input;
using YARG.Core.Logging;
using YARG.Menu.Navigation;
using YARG.Menu.Persistent;
using YARG.Player;
using YARG.Settings;

namespace YARG.Input
{
    using Enumerate = InputControlExtensions.Enumerate;

    public delegate void MenuInputEvent(YargPlayer player, ref GameInput input);

    public static class InputManager
    {
        public const Enumerate DEFAULT_CONTROL_ENUMERATION_FLAGS =
            Enumerate.IgnoreControlsInCurrentState | // Only controls that have changed
            Enumerate.IncludeNoisyControls |         // Constantly-changing controls like accelerometers
            Enumerate.IncludeSyntheticControls;      // Non-physical controls like stick up/down/left/right

        public static event Action<InputDevice> DeviceAdded;
        public static event Action<InputDevice> DeviceRemoved;

        public static event MenuInputEvent MenuInput;

        private static double _latestInputTime;

        /// <summary>
        /// The time to be used for gameplay input updates.
        /// </summary>
        /// <remarks>
        /// This time is the later time between the time marked in <see cref="InputSystem.onBeforeUpdate"/>
        /// and the time of the most recent input event, such that any input events that happen after an
        /// update starts are factored into input updates.
        /// </remarks>
        public static double InputUpdateTime { get; private set; }

        /// <summary>
        /// The instantaneous current time of the input system.
        /// </summary>
        public static double CurrentInputTime => InputState.currentTime;

        private static HashSet<InputDevice> _seenDevices = new();
        private static HashSet<InputDevice> _disabledDevices = new();

        private static HashSet<InputDevice> _registeredDevices = new();

        private static DefaultKeyboardMenuBindings _defaultKeyboardMenuBindings;

        // We do this song and dance of tracking focus changes manually rather than setting
        // InputSettings.backgroundBehavior to IgnoreFocus, so that input is still (largely) disabled when unfocused
        // but devices are not removed only to be re-added when coming back into focus
        private static bool _gameFocused;
        private static bool _focusChanged;
        private static HashSet<InputDevice> _backgroundDisabledDevices = new();

        private static bool HasProfileWithKeyboard =>
            PlayerContainer.Players
                .Where(p => p.InputsEnabled)
                .SelectMany(p => p.Bindings.InputDevices)
                .OfType<Keyboard>()
                .Any();

        public static void Initialize()
        {
            InputSystem.pollingFrequency = SettingsManager.Settings.InputPollingFrequency.Value;

            InputSystem.onEvent += OnEvent;

            InputSystem.onBeforeUpdate += OnBeforeUpdate;
            InputSystem.onAfterUpdate += OnAfterUpdate;

            _gameFocused = Application.isFocused;
            Application.focusChanged += OnFocusChange;
            InputSystem.onDeviceChange += OnDeviceChange;

            _defaultKeyboardMenuBindings = new DefaultKeyboardMenuBindings();

            // Notify of all current devices
            ToastManager.ToastInformation("Devices found: " + (Microphone.devices.Length + InputSystem.devices.Count));
            foreach (var device in InputSystem.devices)
            {
                if (!device.enabled)
                {
                    _disabledDevices.Add(device);
                    continue;
                }

                DeviceAdded?.Invoke(device);
            }

            // Register formatter for device descriptions, we want them to output as JSON in logs
            Utf16ValueStringBuilder.RegisterTryFormat(
                (InputDeviceDescription value, Span<char> destination, out int charsWritten, ReadOnlySpan<char> format) =>
                {
                    string json = value.ToJson();
                    charsWritten = json.Length;
                    return json.AsSpan().TryCopyTo(destination);
                }
            );
        }

        public static void Destroy()
        {
            _defaultKeyboardMenuBindings?.Dispose();
            _defaultKeyboardMenuBindings = null;

            InputSystem.onEvent -= OnEvent;

            InputSystem.onBeforeUpdate -= OnBeforeUpdate;
            InputSystem.onAfterUpdate -= OnAfterUpdate;

            InputSystem.onDeviceChange -= OnDeviceChange;
        }

        public static void RegisterPlayer(YargPlayer player)
        {
            player.MenuInput += OnMenuInput;
            player.Bindings.DeviceAdded += OnPlayerBindingDeviceAdded;
            player.Bindings.DeviceRemoved += OnPlayerBindingDeviceRemoved;

            foreach (var device in player.Bindings.InputDevices)
            {
                if (!_registeredDevices.Add(device))
                {
                    YargLogger.LogFormatError("Player already registered with device: {0}", device);
                }

                if (device is Keyboard)
                {
                    _defaultKeyboardMenuBindings.Disable();
                }
            }
        }

        public static void UnregisterPlayer(YargPlayer player)
        {
            player.MenuInput -= OnMenuInput;
            player.Bindings.DeviceAdded -= OnPlayerBindingDeviceAdded;
            player.Bindings.DeviceRemoved -= OnPlayerBindingDeviceRemoved;

            foreach (var device in player.Bindings.InputDevices)
            {
                if (!_registeredDevices.Remove(device))
                {
                    YargLogger.LogFormatError("Player not registered with device: {0}", device);
                }
            }

            if (!HasProfileWithKeyboard)
            {
                _defaultKeyboardMenuBindings?.Enable();
            }
        }

        private static void OnPlayerBindingDeviceAdded(InputDevice device)
        {
            if (HasProfileWithKeyboard)
            {
                _defaultKeyboardMenuBindings.Disable();
            }
        }

        private static void OnPlayerBindingDeviceRemoved(InputDevice device)
        {
            if (!HasProfileWithKeyboard)
            {
                _defaultKeyboardMenuBindings.Enable();
            }
        }

        public static void OnMenuAction(MenuAction action, bool pressed)
        {
            var input = new GameInput(CurrentInputTime, (int)action, pressed);
            MenuInput?.Invoke(null, ref input);
        }

        private static void OnMenuInput(YargPlayer player, ref GameInput input)
        {
            MenuInput?.Invoke(player, ref input);
        }

        private static void OnBeforeUpdate()
        {
            InputUpdateTime = CurrentInputTime;
        }

        private static void OnAfterUpdate()
        {
            InputUpdateTime = CurrentInputTime;

            if (InputUpdateTime < _latestInputTime)
            {
                YargLogger.LogFormatError(
                    "The last input event for this update is in the future! After-update time: {0}, last input time: {1}",
                    InputUpdateTime, _latestInputTime
                );
            }

            // Update bindings using the input update time
            using (var players = PlayerContainer.PlayerEnumerator)
            {
                while (players.MoveNext())
                {
                    players.Current.Bindings.UpdateBindingsForFrame(InputUpdateTime);
                }
            }

            // Remove any devices that happened to be actually disabled
            // right as the game focus changed
            if (_focusChanged && _gameFocused)
            {
                foreach (var device in _backgroundDisabledDevices)
                {
                    YargLogger.LogFormatDebug("Device disabled: {0}", device.displayName);
                    DeviceRemoved?.Invoke(device);
                }
            }

            _focusChanged = false;
        }

        // For input time handling/debugging
        private static void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            double currentTime = CurrentInputTime;

            // Only check state events
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
            {
                return;
            }

            // Keep track of the latest input event
            if (eventPtr.time > _latestInputTime)
            {
                _latestInputTime = eventPtr.time;
            }

            // Rare edge-case, but the input system very much allows this
            if (eventPtr.time > currentTime)
            {
                YargLogger.LogFormatError(
                    "An input event is in the future!\nCurrent time: {0}, event time: {1}, device: {2}",
                    currentTime, eventPtr.time, device);
            }

            // Ignore keyboard, mouse, pen, touchscreen, and motion sensors for auto-connecting profiles
            if (device is Keyboard or Mouse or Pen or Touchscreen or Pointer or Sensor)
            {
                return;
            }

            if (!_registeredDevices.Contains(device))
            {
                // Don't ask me why, but we get events with no controls changed, so we have to check that there
                // was a change in addition to checking if it was a noisy control that did change
                bool controlChanged = false;

                foreach (var control in eventPtr.EnumerateChangedControls())
                {
                    if (control.noisy || control is not ButtonControl)
                    {
                        continue;
                    }
                    controlChanged = true;
                    break;
                }

                if (controlChanged)
                {
                    eventPtr.handled = PlayerContainer.TryConnectProfile(device);
                }
            }
        }

        private static void OnFocusChange(bool focused)
        {
            _gameFocused = focused;
            _focusChanged = true;
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            switch (change)
            {
                case InputDeviceChange.Added:
                {
                    if (SettingsManager.Settings.InputDeviceLogging.Value)
                    {
                        YargLogger.LogFormatInfo("Device added: {0}\nDescription:\n{1}\n", device.displayName, device.description);
                    }

                    // Don't toast if the device disabled itself
                    if (device.enabled)
                    {
                        ToastManager.ToastMessage($"Device added: {device.displayName}");
                        DeviceAdded?.Invoke(device);
                    }
                    else
                    {
                        _disabledDevices.Add(device);
                    }

                    _seenDevices.Add(device);
                    break;
                }
                case InputDeviceChange.Removed:
                {
                    YargLogger.LogFormatDebug("Device removed: {0}", device.displayName);

                    // Don't toast for disabled devices
                    if (!_disabledDevices.Remove(device))
                    {
                        ToastManager.ToastMessage($"Device removed: {device.displayName}");
                        DeviceRemoved?.Invoke(device);
                    }

                    _seenDevices.Remove(device);
                    break;
                }
                case InputDeviceChange.Enabled:
                {
                    if (_focusChanged || Application.isFocused != _gameFocused)
                    {
                        _focusChanged = true;
                        _backgroundDisabledDevices.Remove(device);
                        return;
                    }

                    YargLogger.LogFormatDebug("Device enabled: {0}", device.displayName);
                    if (_disabledDevices.Remove(device))
                    {
                        ToastManager.ToastMessage($"Device added: {device.displayName}");
                        DeviceAdded?.Invoke(device);
                    }

                    _seenDevices.Add(device);
                    break;
                }
                case InputDeviceChange.Disabled:
                {
                    if (_focusChanged || Application.isFocused != _gameFocused)
                    {
                        _focusChanged = true;
                        _backgroundDisabledDevices.Add(device);
                        return;
                    }

                    YargLogger.LogFormatDebug("Device disabled: {0}", device.displayName);

                    if (!_disabledDevices.Contains(device) && _seenDevices.Contains(device))
                    {
                        ToastManager.ToastMessage($"Device removed: {device.displayName}");
                        DeviceRemoved?.Invoke(device);
                    }

                    _seenDevices.Add(device);
                    _disabledDevices.Add(device);
                    break;
                }
            }
        }
    }
}
