# BluetoothHeartrate

A BLE heartrate sensor module for [VRCOSC] based on the original [HeartrateModule]

[VRCOSC]: https://github.com/VolcanicArts/VRCOSC
[HeartrateModule]: https://github.com/VolcanicArts/VRCOSC/blob/2022.1219.0/VRCOSC.Game/Modules/Modules/Heartrate/HeartRateModule.cs

## How to use

1. Find the latest release on the [Releases] page
2. Download the attached DLL
3. Place the DLL in `%appdata%\VRCOSC\assemblies`
4. Start VRCOSC
5. Scroll to the bottom of the module list
6. Enable "Bluetooth Heartrate" (by clicking on the blank square in front of it)
7. Turn on your heartrate monitor (if it has no separate power button, then simply put in on)
8. Switch to the "Run" screen (play icon) and press the green play button to start the modules
9. Wait for the module to discover your device. You should see log messages like the one below:

    > \[Bluetooth Heartrate]: Discovered device: Polar H10 (MAC: XX:XX:XX:XX:XX:XX)

10. Once you see your device, click the blue button in the top right of the log window to open the log in Notepad
11. Copy the MAC address of the device from the log file (only the `XX:XX:XX:XX:XX:XX` part)
12. Return to VRCOSC and switch back to the module list
13. Find the "Bluetooth Heartrate" module agan
14. Click the cogwheel at the end of the module's line to access its settings
15. Paste the MAC address into the "Device MAC address" setting input
16. Switch back to the "Run" screen and check the log output

If everything went well, you should see the following messages:

> \[Bluetooth Heartrate]: Starting<br>
> \[Bluetooth Heartrate]: Watching for devices<br>
> \[Bluetooth Heartrate]: Started<br>
> \[Bluetooth Heartrate]: Found device for MAC XX:XX:XX:XX:XX:XX<br>
> \[Bluetooth Heartrate]: Found heartrate service<br>
> \[Bluetooth Heartrate]: Found heartrate measurement characteristic<br>
> \[Bluetooth Heartrate]: Registered heartrate characteristic value change handler<br>
> \[Bluetooth Heartrate]: Writing client characteristic configuration descriptor<br>
> \[Bluetooth Heartrate]: Connection successful

[Releases]: https://github.com/DJDavid98/VRCOSC-BluetoothHeartrate/releases

### Known issues

Sometimes Windows' Bluetooth API starts being weird and you will see a "No heartrate characteristic found" in the logs.
This can happen either temporarily (and the module might recover on its own after a few seconds) or it can be a persistent issue.

### Troubleshooting

If something went wrong, try:
* restarting VRCOSC
* turning Bluetooth off and back on in the system settings
* physically disconnet and reconnect the Bluetooth dongle (if it's not built in to your device)
* restarting your PC
* turning your heart rate monitor off and back on (if it's lacking a power button, take it off for a bit, then put it back on)
