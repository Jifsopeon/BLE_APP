namespace BLE_APP.Models;

public enum BluetoothConnectionState
{
    BluetoothUnavailable,
    BluetoothDisabled,
    PermissionRequired,
    Idle,
    Scanning,
    DeviceFound,
    Connecting,
    DiscoveringServices,
    Subscribing,
    Connected,
    ReceivingData,
    Disconnecting,
    Disconnected,
    Reconnecting,
    Error
}
