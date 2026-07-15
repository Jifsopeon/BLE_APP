using Bluetooth.Abstractions.Options;
using Bluetooth.Core.Infrastructure.Retries;
using Bluetooth.Maui.Platforms.Droid.Enums;
using Bluetooth.Maui.Platforms.Droid.Exceptions;
using Bluetooth.Maui.Platforms.Droid.Scanning.Factories;
using Bluetooth.Maui.Platforms.Droid.Scanning.NativeObjects;
using Bluetooth.Maui.Platforms.Droid.Tools;

namespace Bluetooth.Maui.Platforms.Droid.Scanning;

/// <summary>
///     Android implementation of a Bluetooth Low Energy remote characteristic.
///     This class wraps Android's BluetoothGattCharacteristic, providing platform-specific
///     implementations for read, write, notify, and descriptor operations.
/// </summary>
public class AndroidBluetoothRemoteCharacteristic : BaseBluetoothRemoteCharacteristic, BluetoothGattProxy.IBluetoothGattCharacteristicDelegate
{
    /// <summary>
    ///     The CCCD (Client Characteristic Configuration Descriptor) UUID used for enabling/disabling notifications.
    /// </summary>
    private readonly static Guid _cccdUuid = Guid.Parse("00002902-0000-1000-8000-00805f9b34fb");
    private bool _pendingIsListeningWrite;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AndroidBluetoothRemoteCharacteristic" /> class.
    /// </summary>
    /// <param name="remoteService">The Bluetooth service to which this characteristic belongs.</param>
    /// <param name="spec">The factory spec containing characteristic information.</param>
    /// <param name="descriptorFactory">The factory for creating descriptors.</param>
    /// <param name="nameProvider">An optional provider for characteristic names.</param>
    /// <param name="logger">Optional logger for logging characteristic operations.</param>
    public AndroidBluetoothRemoteCharacteristic(IBluetoothRemoteService remoteService, IBluetoothRemoteCharacteristicFactory.BluetoothRemoteCharacteristicFactorySpec spec, IBluetoothRemoteDescriptorFactory descriptorFactory, IBluetoothNameProvider? nameProvider = null, ILogger<IBluetoothRemoteCharacteristic>? logger = null) :
        base(remoteService, spec, descriptorFactory, nameProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec is not AndroidBluetoothRemoteCharacteristicFactorySpec nativeSpec)
        {
            throw new ArgumentException($"Expected spec of type {typeof(AndroidBluetoothRemoteCharacteristicFactorySpec)}, but got {spec.GetType()}");
        }

        NativeCharacteristic = nativeSpec.NativeCharacteristic;
    }

    /// <summary>
    ///     Gets the native Android GATT characteristic.
    /// </summary>
    public BluetoothGattCharacteristic NativeCharacteristic { get; }

    /// <summary>
    ///     Gets the Bluetooth service to which this characteristic belongs, cast to the Android-specific implementation.
    /// </summary>
    public AndroidBluetoothRemoteService AndroidBluetoothRemoteService =>
        (AndroidBluetoothRemoteService) Service;

    /// <summary>
    ///     Gets the GATT proxy from the device.
    /// </summary>
    private BluetoothGattProxy BluetoothGattProxy =>
        AndroidBluetoothRemoteService.AndroidBluetoothRemoteDevice.BluetoothGattProxy ?? throw new InvalidOperationException("Device not connected - GATT proxy is null");

    #region Read

    /// <inheritdoc />
    /// <seealso href="https://developer.android.com/reference/android/bluetooth/BluetoothGatt#readCharacteristic(android.bluetooth.BluetoothGattCharacteristic)">Android BluetoothGatt.readCharacteristic()</seealso>
    protected async override ValueTask NativeReadValueAsync()
    {
        // Get retry options from device connection options, or use default
        var retryOptions = AndroidBluetoothRemoteService.AndroidBluetoothRemoteDevice.ConnectionOptions?.Android?.GattReadRetry
                           ?? new RetryOptions { MaxRetries = 2, DelayBetweenRetries = TimeSpan.FromMilliseconds(100) };

        // Call with configurable retry
        await RetryTools.RunWithRetriesAsync(ReadCharacteristicInternal, retryOptions, CancellationToken.None).ConfigureAwait(false);
    }

    private void ReadCharacteristicInternal()
    {
        var success = BluetoothGattProxy.BluetoothGatt.ReadCharacteristic(NativeCharacteristic);
        if (!success)
        {
            throw new InvalidOperationException("Failed to initiate characteristic read");
        }
    }

    /// <inheritdoc />
    protected override bool NativeCanRead()
    {
        return NativeCharacteristic.Properties.HasFlag(GattProperty.Read);
    }

    #endregion

    #region Write

    /// <inheritdoc />
    /// <seealso href="https://developer.android.com/reference/android/bluetooth/BluetoothGatt#writeCharacteristic(android.bluetooth.BluetoothGattCharacteristic)">Android BluetoothGatt.writeCharacteristic()</seealso>
    protected async override ValueTask NativeWriteValueAsync(ReadOnlyMemory<byte> value)
    {
        // Get retry options from device connection options, or use default
        var retryOptions = AndroidBluetoothRemoteService.AndroidBluetoothRemoteDevice.ConnectionOptions?.Android?.GattWriteRetry ?? RetryOptions.Default;

        // Call with configurable retry
        await RetryTools.RunWithRetriesAsync(() => BluetoothGattCharacteristicWrite(value), retryOptions, CancellationToken.None).ConfigureAwait(false);
    }

    private void BluetoothGattCharacteristicWrite(ReadOnlyMemory<byte> value)
    {
        // Ensure BluetoothGatt exists and is available
        ArgumentNullException.ThrowIfNull(BluetoothGattProxy);

        // Ensure WRITE is supported
        CharacteristicCantWriteException.ThrowIfCantWrite(this);

        // Get WriteType
        NativeCharacteristic.WriteType = GetBluetoothGattCharacteristicWriteType();

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            // Write the value
            var writeResult = (Android.Bluetooth.CurrentBluetoothStatusCodes) BluetoothGattProxy.BluetoothGatt.WriteCharacteristic(NativeCharacteristic, value.ToArray(), (int) GetBluetoothGattCharacteristicWriteType());

            AndroidNativeCurrentBluetoothStatusCodesException.ThrowIfNotSuccess(writeResult);
        }
        else
        {
            // Write the value
            if (!NativeCharacteristic.SetValue(value.ToArray()))
            {
                throw new CharacteristicWriteException(this, value, $"BluetoothGattCharacteristic.SetValue() Failed");
            }

            // Write the characteristic
            if (!BluetoothGattProxy.BluetoothGatt.WriteCharacteristic(NativeCharacteristic))
            {
                throw new CharacteristicWriteException(this, value, "BluetoothGatt.WriteCharacteristic() Failed");
            }
        }
    }

    private GattWriteType GetBluetoothGattCharacteristicWriteType()
    {
        if (NativeCharacteristic.Properties.HasFlag(GattProperty.WriteNoResponse))
        {
            return GattWriteType.NoResponse;
        }

        if (NativeCharacteristic.Properties.HasFlag(GattProperty.SignedWrite))
        {
            return GattWriteType.Signed;
        }

        if (NativeCharacteristic.Properties.HasFlag(GattProperty.Write))
        {
            return GattWriteType.Default;
        }

        throw new UnreachableException("This case should be caught by CharacteristicCantWriteException.ThrowIfCantWrite");
    }

    /// <inheritdoc />
    protected override bool NativeCanWrite()
    {
        return NativeCharacteristic.Properties.HasFlag(GattProperty.Write) || NativeCharacteristic.Properties.HasFlag(GattProperty.WriteNoResponse) || NativeCharacteristic.Properties.HasFlag(GattProperty.SignedWrite);
    }

    /// <summary>
    ///     Gets the Android-specific write capability string representation for the characteristic.
    /// </summary>
    /// <returns>
    ///     Returns "WNR" for write without response, "WS" for signed writes, "W" for standard write,
    ///     or an empty string if no write operations are supported.
    /// </returns>
    protected override string ToWriteString()
    {
        if (NativeCharacteristic.Properties.HasFlag(GattProperty.WriteNoResponse))
        {
            return "WNR";
        }

        if (NativeCharacteristic.Properties.HasFlag(GattProperty.SignedWrite))
        {
            return "WS";
        }

        if (NativeCharacteristic.Properties.HasFlag(GattProperty.Write))
        {
            return "W";
        }

        return string.Empty;
    }

    #endregion

    #region Listen (Notifications/Indications)

    /// <inheritdoc />
    protected override bool NativeCanListen()
    {
        return NativeCharacteristic.Properties.HasFlag(GattProperty.Notify) || NativeCharacteristic.Properties.HasFlag(GattProperty.Indicate);
    }

    /// <inheritdoc />
    protected async override ValueTask NativeReadIsListeningAsync()
    {
        // On Android, we need to check the CCCD descriptor to determine if notifications are enabled
        var descriptor = GetDescriptor(_cccdUuid);
        if (descriptor == null)
        {
            // No CCCD descriptor found, assume not listening
            OnReadIsListeningSucceeded(false);
            return;
        }

        if (!descriptor.CanRead)
        {
            // Some peripherals expose CCCD as write-only. Reading CCCD is only an "already listening" pre-check;
            // notification enablement is determined by the descriptor write callback.
            OnReadIsListeningSucceeded(IsListening);
            return;
        }

        var cccdValue = await descriptor.ReadValueAsync().ConfigureAwait(false);
        var cccdValueArray = cccdValue.ToArray();
        var isListening = cccdValueArray.SequenceEqual(BluetoothGattDescriptor.EnableNotificationValue?.ToArray() ?? []) || cccdValueArray.SequenceEqual(BluetoothGattDescriptor.EnableIndicationValue?.ToArray() ?? []);
        OnReadIsListeningSucceeded(isListening);
    }

    /// <inheritdoc />
    /// <seealso href="https://developer.android.com/reference/android/bluetooth/BluetoothGatt#setCharacteristicNotification(android.bluetooth.BluetoothGattCharacteristic,%20boolean)">Android BluetoothGatt.setCharacteristicNotification()</seealso>
    protected async override ValueTask NativeWriteIsListeningAsync(bool shouldBeListening)
    {
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        LogBleNotify($"[BLE-NOTIFY] Native notification setup started; ApiLevel={(int) Android.OS.Build.VERSION.SdkInt}; DeviceId={Service.Device.Id}; ServiceUuid={Service.Id}; CharacteristicUuid={Id}; ThreadId={System.Environment.CurrentManagedThreadId}");

        // First, enable/disable notifications locally on the GATT object
        LogBleNotify($"[BLE-NOTIFY] SetCharacteristicNotification invocation; DeviceId={Service.Device.Id}; CharacteristicUuid={Id}; Enabled={shouldBeListening}; NativeObjectType={NativeCharacteristic.GetType().FullName}; ThreadId={System.Environment.CurrentManagedThreadId}");
        var success = BluetoothGattProxy.BluetoothGatt.SetCharacteristicNotification(NativeCharacteristic, shouldBeListening);
        LogBleNotify($"[BLE-NOTIFY] SetCharacteristicNotification result; DeviceId={Service.Device.Id}; CharacteristicUuid={Id}; Result={success}; ElapsedMs={GetElapsedMilliseconds(startedAt):0.0}");

        if (!success)
        {
            throw new InvalidOperationException($"Failed to {(shouldBeListening ? "enable" : "disable")} characteristic notification");
        }

        var descriptor = GetDescriptor(_cccdUuid);
        if (descriptor == null)
        {
            throw new InvalidOperationException("CCCD descriptor not found for this characteristic");
        }

        if (descriptor is not AndroidBluetoothRemoteDescriptor androidDescriptor)
        {
            throw new InvalidOperationException($"CCCD descriptor is not an Android descriptor. DescriptorType={descriptor.GetType().FullName}");
        }

        byte[] cccdValue;
        string selectedMode;
        if (!shouldBeListening)
        {
            // Disable notifications
            cccdValue = BluetoothGattDescriptor.DisableNotificationValue?.ToArray() ?? [];
            selectedMode = "Disable";
        }
        else if (NativeCharacteristic.Properties.HasFlag(GattProperty.Notify))
        {
            // Enable notifications
            cccdValue = BluetoothGattDescriptor.EnableNotificationValue?.ToArray() ?? [];
            selectedMode = "Notify";
        }
        else if (NativeCharacteristic.Properties.HasFlag(GattProperty.Indicate))
        {
            // Enable indications
            cccdValue = BluetoothGattDescriptor.EnableIndicationValue?.ToArray() ?? [];
            selectedMode = "Indicate";
        }
        else
        {
            throw new InvalidOperationException("Characteristic does not support Notify or Indicate, cannot enable notifications");
        }

        if (cccdValue.Length != 2)
        {
            throw new InvalidOperationException($"Invalid CCCD payload length {cccdValue.Length} for {selectedMode}.");
        }

        _pendingIsListeningWrite = shouldBeListening;

        LogBleNotify($"[BLE-NOTIFY] Characteristic native properties; DeviceId={Service.Device.Id}; ServiceUuid={Service.Id}; CharacteristicUuid={Id}; Properties={NativeCharacteristic.Properties}; CanListen={CanListen}; IsListening={IsListening}");
        LogBleNotify($"[BLE-NOTIFY] CCCD discovered; DeviceId={Service.Device.Id}; ServiceUuid={Service.Id}; CharacteristicUuid={Id}; DescriptorUuid={descriptor.Id}; NativeObjectType={androidDescriptor.NativeDescriptor.GetType().FullName}");
        LogBleNotify($"[BLE-NOTIFY] Native descriptor permissions; DeviceId={Service.Device.Id}; DescriptorUuid={descriptor.Id}; Permissions={androidDescriptor.NativeDescriptor.Permissions}");
        LogBleNotify($"[BLE-NOTIFY] Wrapper CanRead; DeviceId={Service.Device.Id}; DescriptorUuid={descriptor.Id}; CanRead={descriptor.CanRead}");
        LogBleNotify($"[BLE-NOTIFY] Wrapper CanWrite; DeviceId={Service.Device.Id}; DescriptorUuid={descriptor.Id}; CanWrite={descriptor.CanWrite}");
        LogBleNotify($"[BLE-NOTIFY] Selected mode; DeviceId={Service.Device.Id}; CharacteristicUuid={Id}; Mode={selectedMode}");
        LogBleNotify($"[BLE-NOTIFY] Selected CCCD payload; DeviceId={Service.Device.Id}; DescriptorUuid={descriptor.Id}; Payload={Convert.ToHexString(cccdValue)}");
        LogBleNotify($"[BLE-NOTIFY] Invoking Android WriteDescriptor; DeviceId={Service.Device.Id}; DescriptorUuid={descriptor.Id}; ApiLevel={(int) Android.OS.Build.VERSION.SdkInt}; ThreadId={System.Environment.CurrentManagedThreadId}");

        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            var writeResult = (Android.Bluetooth.CurrentBluetoothStatusCodes) BluetoothGattProxy.BluetoothGatt.WriteDescriptor(androidDescriptor.NativeDescriptor, cccdValue);
            LogBleNotify($"[BLE-NOTIFY] WriteDescriptor request result; DeviceId={Service.Device.Id}; DescriptorUuid={descriptor.Id}; Result={writeResult}; ElapsedMs={GetElapsedMilliseconds(startedAt):0.0}");
            AndroidNativeCurrentBluetoothStatusCodesException.ThrowIfNotSuccess(writeResult);
        }
        else
        {
            if (!androidDescriptor.NativeDescriptor.SetValue(cccdValue))
            {
                throw new DescriptorWriteException(descriptor, cccdValue, "BluetoothGattDescriptor.SetValue() Failed");
            }

            var writeQueued = BluetoothGattProxy.BluetoothGatt.WriteDescriptor(androidDescriptor.NativeDescriptor);
            LogBleNotify($"[BLE-NOTIFY] WriteDescriptor request result; DeviceId={Service.Device.Id}; DescriptorUuid={descriptor.Id}; Result={writeQueued}; ElapsedMs={GetElapsedMilliseconds(startedAt):0.0}");
            if (!writeQueued)
            {
                throw new DescriptorWriteException(descriptor, cccdValue, "BluetoothGatt.WriteDescriptor() Failed");
            }
        }

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    #endregion

    #region Reliable Write

    /// <inheritdoc />
    protected override ValueTask NativeBeginReliableWriteAsync()
    {
        var success = BluetoothGattProxy.BluetoothGatt.BeginReliableWrite();
        if (!success)
        {
            throw new InvalidOperationException("Failed to begin reliable write");
        }

        // BeginReliableWrite is synchronous - signal success immediately
        OnBeginReliableWriteSucceeded();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected async override ValueTask NativeExecuteReliableWriteAsync()
    {
        var success = BluetoothGattProxy.BluetoothGatt.ExecuteReliableWrite();
        if (!success)
        {
            throw new InvalidOperationException("Failed to execute reliable write");
        }

        // ExecuteReliableWrite is asynchronous - wait for the device callback
        var device = AndroidBluetoothRemoteService.AndroidBluetoothRemoteDevice;

        // Use a reasonable timeout (e.g., 30 seconds) for the native callback
        var timeout = TimeSpan.FromSeconds(30);
        var waitSucceeded = await device.WaitForReliableWriteCompletedAsync(timeout).ConfigureAwait(false);

        if (!waitSucceeded)
        {
            throw new TimeoutException("Reliable write execute operation timed out waiting for Android callback");
        }

        // Signal success to complete the base class TCS
        OnExecuteReliableWriteSucceeded();
    }

    /// <inheritdoc />
    protected override ValueTask NativeAbortReliableWriteAsync()
    {
        BluetoothGattProxy.BluetoothGatt.AbortReliableWrite();

        // AbortReliableWrite is synchronous and void - it always succeeds
        OnAbortReliableWriteSucceeded();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Descriptors

    /// <inheritdoc />
    protected override ValueTask NativeDescriptorsExplorationAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // On Android, descriptors are discovered along with characteristics
            // The NativeCharacteristic.Descriptors property should already contain all descriptors
            var descriptors = NativeCharacteristic.Descriptors ?? new List<BluetoothGattDescriptor>();
            OnDescriptorsExplorationSucceeded(descriptors, AreRepresentingTheSameObject, FromInputTypeToOutputTypeConversion);
        }
        catch (Exception ex)
        {
            OnDescriptorsExplorationFailed(ex);
        }

        return ValueTask.CompletedTask;

        IBluetoothRemoteDescriptor FromInputTypeToOutputTypeConversion(BluetoothGattDescriptor nativeDescriptor)
        {
            var spec = new AndroidBluetoothRemoteDescriptorFactorySpec(nativeDescriptor);
            return (DescriptorFactory ?? throw new InvalidOperationException("DescriptorFactory must be initialized via the spec-based constructor.")).Create(this, spec);
        }
    }

    private static bool AreRepresentingTheSameObject(BluetoothGattDescriptor native, IBluetoothRemoteDescriptor shared)
    {
        return shared is AndroidBluetoothRemoteDescriptor androidDescriptor && native.Uuid?.Equals(androidDescriptor.NativeDescriptor.Uuid) == true;
    }

    #endregion

    #region BluetoothGattProxy.IBluetoothGattCharacteristicDelegate Implementation

    /// <inheritdoc />
    public void OnCharacteristicChanged(BluetoothGattCharacteristic? nativeCharacteristic, byte[]? value)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(nativeCharacteristic);

            // Verify this is the correct characteristic
            if (!NativeCharacteristic.Uuid?.Equals(nativeCharacteristic.Uuid) ?? true)
            {
                return; // Not for this characteristic
            }

            OnReadValueSucceeded(value ?? []);
        }
        catch (Exception e)
        {
            BluetoothUnhandledExceptionListener.OnBluetoothUnhandledException(this, e);
        }
    }

    /// <inheritdoc />
    public void OnCharacteristicWrite(GattStatus status, BluetoothGattCharacteristic? nativeCharacteristic)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(nativeCharacteristic);

            // Verify this is the correct characteristic
            if (!NativeCharacteristic.Uuid?.Equals(nativeCharacteristic.Uuid) ?? true)
            {
                return; // Not for this characteristic
            }

            if (status != GattStatus.Success)
            {
                OnWriteValueFailed(new AndroidNativeGattCallbackStatusException((GattCallbackStatus) status));
                return;
            }

            OnWriteValueSucceeded();
        }
        catch (Exception e)
        {
            OnWriteValueFailed(e);
        }
    }

    /// <inheritdoc />
    public void OnCharacteristicRead(GattStatus status, BluetoothGattCharacteristic? nativeCharacteristic, byte[]? value)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(nativeCharacteristic);

            // Verify this is the correct characteristic
            if (!NativeCharacteristic.Uuid?.Equals(nativeCharacteristic.Uuid) ?? true)
            {
                return; // Not for this characteristic
            }

            if (status != GattStatus.Success)
            {
                OnReadValueFailed(new AndroidNativeGattCallbackStatusException((GattCallbackStatus) status));
                return;
            }

            OnReadValueSucceeded(value ?? []);
        }
        catch (Exception e)
        {
            OnReadValueFailed(e);
        }
    }

    /// <inheritdoc />
    public void OnDescriptorRead(GattStatus status, BluetoothGattDescriptor? nativeDescriptor, byte[]? value)
    {
        // Forward to the appropriate descriptor
        if (nativeDescriptor == null)
        {
            return;
        }

        try
        {
            var descriptor = GetDescriptorOrDefault(d => d is AndroidBluetoothRemoteDescriptor androidDesc && androidDesc.NativeDescriptor.Uuid?.Equals(nativeDescriptor.Uuid) == true);

            if (descriptor is AndroidBluetoothRemoteDescriptor androidDescriptor)
            {
                androidDescriptor.NotifyDescriptorRead(status, value);
            }
        }
        catch (Exception e)
        {
            BluetoothUnhandledExceptionListener.OnBluetoothUnhandledException(this, e);
        }
    }

    /// <inheritdoc />
    public void OnDescriptorWrite(GattStatus status, BluetoothGattDescriptor? nativeDescriptor)
    {
        // Check if this is the CCCD descriptor for notifications
        if (nativeDescriptor?.Uuid?.ToGuid().Equals(_cccdUuid) == true)
        {
            LogBleNotify($"[BLE-NOTIFY] OnDescriptorWrite callback; DeviceId={Service.Device.Id}; ServiceUuid={Service.Id}; CharacteristicUuid={Id}; DescriptorUuid={_cccdUuid}; GATT status={status}; ThreadId={System.Environment.CurrentManagedThreadId}");
            // This is a CCCD write for enabling/disabling notifications
            if (status != GattStatus.Success)
            {
                OnWriteIsListeningFailed(new AndroidNativeGattCallbackStatusException((GattCallbackStatus) status));
                return;
            }

            IsListening = _pendingIsListeningWrite;
            LogBleNotify($"[BLE-NOTIFY] Subscription active; DeviceId={Service.Device.Id}; CharacteristicUuid={Id}; IsListening={IsListening}");
            OnWriteIsListeningSucceeded();
            return;
        }

        // Forward to the appropriate descriptor
        if (nativeDescriptor == null)
        {
            return;
        }

        try
        {
            var descriptor = GetDescriptorOrDefault(d => d is AndroidBluetoothRemoteDescriptor androidDesc && androidDesc.NativeDescriptor.Uuid?.Equals(nativeDescriptor.Uuid) == true);

            if (descriptor is AndroidBluetoothRemoteDescriptor androidDescriptor)
            {
                androidDescriptor.NotifyDescriptorWrite(status);
            }
        }
        catch (Exception e)
        {
            BluetoothUnhandledExceptionListener.OnBluetoothUnhandledException(this, e);
        }
    }

    #endregion

    private static double GetElapsedMilliseconds(long startedAt)
        => System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

    private static void LogBleNotify(string message)
    {
        System.Diagnostics.Debug.WriteLine(message);
        Android.Util.Log.Info("BLE-NOTIFY", message);
    }

}
