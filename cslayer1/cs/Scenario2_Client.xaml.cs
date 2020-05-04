//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Input.Preview.Injection;
using Windows.UI.Xaml.Input;
using Windows.System;

namespace SDKTemplate
{
    // This scenario connects to the device selected in the "Discover
    // GATT Servers" scenario and communicates with it.
    // Note that this scenario is rather artificial because it communicates
    // with an unknown service with unknown characteristics.
    // In practice, your app will be interested in a specific service with
    // a specific characteristic.
    public sealed partial class Scenario2_Client : Page
    {
        public MainPage rootPage = MainPage.Current;

        private ObservableCollection<BluetoothLEAttributeDisplay> ServiceCollection = new ObservableCollection<BluetoothLEAttributeDisplay>();
        private ObservableCollection<BluetoothLEAttributeDisplay> CharacteristicCollection = new ObservableCollection<BluetoothLEAttributeDisplay>();
        

        private BluetoothLEDevice bluetoothLeDevice = null;
        private GattCharacteristic selectedCharacteristic;

        // Only one registered characteristic at a time.
        private GattCharacteristic registeredCharacteristic;
        private GattPresentationFormat presentationFormat;

        bool keepReading = true;

        #region Virtual Keyboard
        private InputInjector inputInjector = InputInjector.TryCreate();
        private InjectedInputKeyboardInfo vKeyBoardInfo1 = new InjectedInputKeyboardInfo();
        private InjectedInputKeyboardInfo vKeyBoardInfo2 = new InjectedInputKeyboardInfo();
        private InjectedInputKeyboardInfo vKeyBoardInfo3 = new InjectedInputKeyboardInfo();
        private InjectedInputKeyboardInfo vKeyBoardInfo4 = new InjectedInputKeyboardInfo();

        

        List<InjectedInputKeyboardInfo> vKeyBoardInfoList = new List<InjectedInputKeyboardInfo>();


        
        /*public void StrikeUp()
        {
            //inject the sequence of keys to the virtual keyboard, finally
            //inputInjector.InjectKeyboardInput(vKeyBoardInfoList); 
        }*/
        
        #endregion

        #region Error Codes
        readonly int E_BLUETOOTH_ATT_WRITE_NOT_PERMITTED = unchecked((int)0x80650003);
                readonly int E_BLUETOOTH_ATT_INVALID_PDU = unchecked((int)0x80650004);
                readonly int E_ACCESSDENIED = unchecked((int)0x80070005);
                readonly int E_DEVICE_NOT_AVAILABLE = unchecked((int)0x800710df); // HRESULT_FROM_WIN32(ERROR_DEVICE_NOT_AVAILABLE)
                #endregion

        #region UI Code
        public Scenario2_Client()
        {
            InitializeComponent(); //[maybe] sends XAML URI  to System.x.y.LoadComponent() to make an object
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {

            if (string.IsNullOrEmpty(rootPage.SelectedBleDeviceId))
            {
                ConnectButton.IsEnabled = false;
            }

            //connect to the bluetooth automatically
            ConnectButton_Click();
            //jgw start up the TimedKeyboardManager utility class
            TimedKeyboardManager.Start(); //note this is a static class (i.e. not instantiated)
                       
            
        }

        protected override async void OnNavigatedFrom(NavigationEventArgs e)
        {
            var success = await ClearBluetoothLEDeviceAsync();
            if (!success)
            {
                rootPage.NotifyUser("Error: Unable to reset app state", NotifyType.ErrorMessage);
            }
        }
        #endregion

        #region Enumerating Services
        private async Task<bool> ClearBluetoothLEDeviceAsync()
        {
            if (subscribedForNotifications)
            {
                // Need to clear the CCCD from the remote device so we stop receiving notifications
                var result = await registeredCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
                if (result != GattCommunicationStatus.Success)
                {
                    return false;
                }
                else
                {
                    selectedCharacteristic.ValueChanged -= Characteristic_ValueChanged;
                    subscribedForNotifications = false;
                }
            }
            bluetoothLeDevice?.Dispose();
            bluetoothLeDevice = null;
            return true;
        }

        private async void ConnectButton_Click()
        {
            ConnectButton.IsEnabled = false;

            if (!await ClearBluetoothLEDeviceAsync()) //this clear any prior device info; a reset
            {
                rootPage.NotifyUser("Error: Unable to reset state, try again.", NotifyType.ErrorMessage);
                ConnectButton.IsEnabled = false;
                return;
            }

            try
            {
                // BT_Code: BluetoothLEDevice.FromIdAsync must be called from a UI thread because it may prompt for consent.

                //bluetoothLeDevice = null;
                bluetoothLeDevice = await BluetoothLEDevice.FromIdAsync(rootPage.SelectedBleDeviceId);

                if (bluetoothLeDevice == null)
                {
                    rootPage.NotifyUser("Failed to connect to device.", NotifyType.ErrorMessage);
                }
            }
            catch (Exception ex) when (ex.HResult == E_DEVICE_NOT_AVAILABLE)
            {
                rootPage.NotifyUser("Bluetooth radio is not on.", NotifyType.ErrorMessage);
            }

            if (bluetoothLeDevice != null)
            {
                // Note: BluetoothLEDevice.GattServices property will return an empty list for unpaired devices. For all uses we recommend using the GetGattServicesAsync method.
                // BT_Code: GetGattServicesAsync returns a list of all the supported services of the device (even if it's not paired to the system).
                // If the services supported by the device are expected to change during BT usage, subscribe to the GattServicesChanged event.
                GattDeviceServicesResult result = await bluetoothLeDevice.GetGattServicesAsync(BluetoothCacheMode.Uncached);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    var services = result.Services;
                    rootPage.NotifyUser(String.Format("Found {0} services", services.Count), NotifyType.StatusMessage);
                    foreach (var service in services)
                    {
                        ServiceCollection.Add(new BluetoothLEAttributeDisplay(service));
                        //JGW working on this to pick the right service by default...
                        BluetoothLEAttributeDisplay testAttributeVar = new BluetoothLEAttributeDisplay(service);

                        //JGW see if the service has the right name.
                        if (testAttributeVar.Name == "SimpleKeyService")
                        {
                            //JGW if so, attempt to auto-select the right characteristics
                            #region AttemptedAutoSelect 
                            
                            CharacteristicCollection.Clear();
                            RemoveValueChangedHandler();
                            
                            IReadOnlyList<GattCharacteristic> characteristics = null;
                            try
                            {
                                // Ensure we have access to the device.
                                var accessStatus = await testAttributeVar.service.RequestAccessAsync();
                                if (accessStatus == DeviceAccessStatus.Allowed)
                                {
                                    // BT_Code: Get all the child characteristics of a service. Use the cache mode to specify uncached characterstics only 
                                    // and the new Async functions to get the characteristics of unpaired devices as well. 
                                    var resultCharacteristics = await testAttributeVar.service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                                    if (resultCharacteristics.Status == GattCommunicationStatus.Success)
                                    {
                                        
                                        characteristics = resultCharacteristics.Characteristics;
                                    }
                                    else
                                    {
                                        rootPage.NotifyUser("Error accessing service.", NotifyType.ErrorMessage);

                                        // On error, act as if there are no characteristics.
                                        characteristics = new List<GattCharacteristic>();
                                    }
                                }
                                else
                                {
                                    // Not granted access
                                    rootPage.NotifyUser("Error accessing service.", NotifyType.ErrorMessage);

                                    // On error, act as if there are no characteristics.
                                    characteristics = new List<GattCharacteristic>();

                                }
                            }
                            catch (Exception ex)
                            {
                                rootPage.NotifyUser("Restricted service. Can't read characteristics: " + ex.Message,
                                    NotifyType.ErrorMessage);
                                // On error, act as if there are no characteristics.
                                characteristics = new List<GattCharacteristic>();
                            }

                            foreach (GattCharacteristic c in characteristics)
                            {
                                //JGW next step to auto-select the characterists
                                //BluetoothLEAttributeDisplay attributeInfoDisp = new BluetoothLEAttributeDisplay();
                                CharacteristicCollection.Add(new BluetoothLEAttributeDisplay(c));
                                BluetoothLEAttributeDisplay testAttributeChars = new BluetoothLEAttributeDisplay(c);
                                if (testAttributeChars.Name=="SimpleKeyState")
                                {
                                    selectedCharacteristic = testAttributeChars.characteristic;
                                    if (selectedCharacteristic == null)
                                    {
                                        rootPage.NotifyUser("No characteristic selected", NotifyType.ErrorMessage);
                                        return;
                                    }

                                    //simulate a subscription button click
                                    ValueChangedSubscribeToggle_Click();
                                }


                            }
                            CharacteristicList.Visibility = Visibility.Visible;
                        }



                                #endregion

                    }
                    ConnectButton.Visibility = Visibility.Collapsed;
                    ServiceList.Visibility = Visibility.Visible;
                }
                else
                {
                    rootPage.NotifyUser("Device unreachable", NotifyType.ErrorMessage);
                }
            }
            ConnectButton.IsEnabled = true;
        }
        #endregion

        #region Enumerating Characteristics
        private async void ServiceList_SelectionChanged()
        {
            var attributeInfoDisp = (BluetoothLEAttributeDisplay)ServiceList.SelectedItem;

            CharacteristicCollection.Clear();
            RemoveValueChangedHandler();

            IReadOnlyList<GattCharacteristic> characteristics = null;
            try
            {
                // Ensure we have access to the device.
                var accessStatus = await attributeInfoDisp.service.RequestAccessAsync();
                if (accessStatus == DeviceAccessStatus.Allowed)
                {
                    // BT_Code: Get all the child characteristics of a service. Use the cache mode to specify uncached characterstics only 
                    // and the new Async functions to get the characteristics of unpaired devices as well. 
                    var result = await attributeInfoDisp.service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
                    if (result.Status == GattCommunicationStatus.Success)
                    {
                        //JGW need to investigate this object set
                        characteristics = result.Characteristics;
                    }
                    else
                    {
                        rootPage.NotifyUser("Error accessing service.", NotifyType.ErrorMessage);

                        // On error, act as if there are no characteristics.
                        characteristics = new List<GattCharacteristic>();
                    }
                }
                else
                {
                    // Not granted access
                    rootPage.NotifyUser("Error accessing service.", NotifyType.ErrorMessage);

                    // On error, act as if there are no characteristics.
                    characteristics = new List<GattCharacteristic>();

                }
            }
            catch (Exception ex)
            {
                rootPage.NotifyUser("Restricted service. Can't read characteristics: " + ex.Message,
                    NotifyType.ErrorMessage);
                // On error, act as if there are no characteristics.
                characteristics = new List<GattCharacteristic>();
            }

            foreach (GattCharacteristic c in characteristics)
            {
                CharacteristicCollection.Add(new BluetoothLEAttributeDisplay(c));
            }
            CharacteristicList.Visibility = Visibility.Visible;
        }
        #endregion


        //JGW attempt to determine key press status
        private void OnKeyDownHandler(Object sender, KeyRoutedEventArgs e)
        {
            if(e.Key == Windows.System.VirtualKey.Up)
            {
                rootPage.NotifyUser("Key is down", NotifyType.ErrorMessage);
            }
        }


        private void AddValueChangedHandler()
        {
            ValueChangedSubscribeToggle.Content = "Unsubscribe from value changes";
            if (!subscribedForNotifications)
            {
                registeredCharacteristic = selectedCharacteristic;
                registeredCharacteristic.ValueChanged += Characteristic_ValueChanged;
                subscribedForNotifications = true;
            }
        }

        private void RemoveValueChangedHandler()
        {
            ValueChangedSubscribeToggle.Content = "Subscribe to value changes";
            if (subscribedForNotifications)
            {
                registeredCharacteristic.ValueChanged -= Characteristic_ValueChanged;
                registeredCharacteristic = null;
                subscribedForNotifications = false;
            }
        }

        private async void CharacteristicList_SelectionChanged()
        {
            selectedCharacteristic = null; 

            var attributeInfoDisp = (BluetoothLEAttributeDisplay)CharacteristicList.SelectedItem;
            if (attributeInfoDisp == null)
            {
                EnableCharacteristicPanels(GattCharacteristicProperties.None);
                return;
            }

            selectedCharacteristic = attributeInfoDisp.characteristic;
            if (selectedCharacteristic == null)
            {
                rootPage.NotifyUser("No characteristic selected", NotifyType.ErrorMessage);
                return;
            }

            // Get all the child descriptors of a characteristics. Use the cache mode to specify uncached descriptors only 
            // and the new Async functions to get the descriptors of unpaired devices as well. 
            var result = await selectedCharacteristic.GetDescriptorsAsync(BluetoothCacheMode.Uncached);
            if (result.Status != GattCommunicationStatus.Success)
            {
                rootPage.NotifyUser("Descriptor read failure: " + result.Status.ToString(), NotifyType.ErrorMessage);
            }

            // BT_Code: There's no need to access presentation format unless there's at least one. 
            presentationFormat = null;
            if (selectedCharacteristic.PresentationFormats.Count > 0)
            {

                if (selectedCharacteristic.PresentationFormats.Count.Equals(1))
                {
                    // Get the presentation format since there's only one way of presenting it
                    presentationFormat = selectedCharacteristic.PresentationFormats[0];
                }
                else
                {
                    // It's difficult to figure out how to split up a characteristic and encode its different parts properly.
                    // In this case, we'll just encode the whole thing to a string to make it easy to print out.
                }
            }

            // Enable/disable operations based on the GattCharacteristicProperties.
            EnableCharacteristicPanels(selectedCharacteristic.CharacteristicProperties);
        }

        private void SetVisibility(UIElement element, bool visible)
        {
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void EnableCharacteristicPanels(GattCharacteristicProperties properties)
        {
            // BT_Code: Hide the controls which do not apply to this characteristic.
            SetVisibility(CharacteristicReadButton, properties.HasFlag(GattCharacteristicProperties.Read));

            SetVisibility(CharacteristicWritePanel,
                properties.HasFlag(GattCharacteristicProperties.Write) ||
                properties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse));
            CharacteristicWriteValue.Text = "";

            SetVisibility(ValueChangedSubscribeToggle, properties.HasFlag(GattCharacteristicProperties.Indicate) ||
                                                       properties.HasFlag(GattCharacteristicProperties.Notify));

        }

        private async void CharacteristicReadButton_Click()
        {
            // BT_Code: Read the actual value from the device by using Uncached.
            GattReadResult result = await selectedCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            if (result.Status == GattCommunicationStatus.Success)
            {
                string formattedResult = FormatValueByPresentation(result.Value, presentationFormat);
                rootPage.NotifyUser($"Read result: {formattedResult}", NotifyType.StatusMessage);
            }
            else
            {
                rootPage.NotifyUser($"Read failed: {result.Status}", NotifyType.ErrorMessage);
            }

            keepReadingValues();


        }

        private void keepReadingValues()
        {
            var t = Task.Run( async () =>
               {
                    async void characteristicRead()
                    {
                        // BT_Code: Read the actual value from the device by using Uncached.
                        GattReadResult result = await selectedCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (result.Status == GattCommunicationStatus.Success)
                        {
                            string formattedResult = FormatValueByPresentation(result.Value, presentationFormat);
                            rootPage.NotifyUser($"Read result: {formattedResult}", NotifyType.StatusMessage);
                            Debug.WriteLine("Read Suceeded");
                        }
                        else
                        {
                            rootPage.NotifyUser($"Read failed: {result.Status}", NotifyType.ErrorMessage);
                            Debug.WriteLine("Read failed");
                        }

                    }

                    //jgw 5/1/20, may need an outer while loop that keeps reconnecting with device if no recent read  


                    while (keepReading == true)
                    {
                        characteristicRead();
                        await Task.Delay(150);
                    }


                });

                
           
        }

        private async void CharacteristicWriteButton_Click()
        {
            //JGW this to stop repeat readings
            keepReading = false;
            if (!String.IsNullOrEmpty(CharacteristicWriteValue.Text))
            {
                var writeBuffer = CryptographicBuffer.ConvertStringToBinary(CharacteristicWriteValue.Text,
                    BinaryStringEncoding.Utf8);

                var writeSuccessful = await WriteBufferToSelectedCharacteristicAsync(writeBuffer);
            }
            else
            {
                rootPage.NotifyUser("No data to write to device", NotifyType.ErrorMessage);
            }
        }

        private async void CharacteristicWriteButtonInt_Click()
        {
            if (!String.IsNullOrEmpty(CharacteristicWriteValue.Text))
            {
                var isValidValue = Int32.TryParse(CharacteristicWriteValue.Text, out int readValue);
                if (isValidValue)
                {
                    var writer = new DataWriter();
                    writer.ByteOrder = ByteOrder.LittleEndian;
                    writer.WriteInt32(readValue);

                    var writeSuccessful = await WriteBufferToSelectedCharacteristicAsync(writer.DetachBuffer());
                }
                else
                {
                    rootPage.NotifyUser("Data to write has to be an int32", NotifyType.ErrorMessage);
                }
            }
            else
            {
                rootPage.NotifyUser("No data to write to device", NotifyType.ErrorMessage);
            }
        }



        private async Task<bool> WriteBufferToSelectedCharacteristicAsync(IBuffer buffer)
        {
            try
            {
                // BT_Code: Writes the value from the buffer to the characteristic.
                var result = await selectedCharacteristic.WriteValueWithResultAsync(buffer);

                if (result.Status == GattCommunicationStatus.Success)
                {
                    rootPage.NotifyUser("Successfully wrote value to device", NotifyType.StatusMessage);
                    return true;
                }
                else
                {
                    rootPage.NotifyUser($"Write failed: {result.Status}", NotifyType.ErrorMessage);
                    return false;
                }
            }
            catch (Exception ex) when (ex.HResult == E_BLUETOOTH_ATT_INVALID_PDU)
            {
                rootPage.NotifyUser(ex.Message, NotifyType.ErrorMessage);
                return false;
            }
            catch (Exception ex) when (ex.HResult == E_BLUETOOTH_ATT_WRITE_NOT_PERMITTED || ex.HResult == E_ACCESSDENIED)
            {
                // This usually happens when a device reports that it support writing, but it actually doesn't.
                rootPage.NotifyUser(ex.Message, NotifyType.ErrorMessage);
                return false;
            }
        }

        private bool subscribedForNotifications = false;
        private async void ValueChangedSubscribeToggle_Click()
        {
            if (!subscribedForNotifications)
            {
                // initialize status
                GattCommunicationStatus status = GattCommunicationStatus.Unreachable;
                var cccdValue = GattClientCharacteristicConfigurationDescriptorValue.None;
                if (selectedCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate))
                {
                    cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Indicate;
                }

                else if (selectedCharacteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                {
                    cccdValue = GattClientCharacteristicConfigurationDescriptorValue.Notify;
                }

                try
                {
                    // BT_Code: Must write the CCCD in order for server to send indications.
                    // We receive them in the ValueChanged event handler.
                    status = await selectedCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(cccdValue);

                    if (status == GattCommunicationStatus.Success)
                    {
                        AddValueChangedHandler();
                        rootPage.NotifyUser("Successfully subscribed for value changes", NotifyType.StatusMessage);
                    }
                    else
                    {
                        rootPage.NotifyUser($"Error registering for value changes: {status}", NotifyType.ErrorMessage);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // This usually happens when a device reports that it support indicate, but it actually doesn't.
                    rootPage.NotifyUser(ex.Message, NotifyType.ErrorMessage);
                }
            }
            else
            {
                try
                {
                    // BT_Code: Must write the CCCD in order for server to send notifications.
                    // We receive them in the ValueChanged event handler.
                    // Note that this sample configures either Indicate or Notify, but not both.
                    var result = await
                            selectedCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                                GattClientCharacteristicConfigurationDescriptorValue.None);
                    if (result == GattCommunicationStatus.Success)
                    {
                        subscribedForNotifications = false;
                        RemoveValueChangedHandler();
                        rootPage.NotifyUser("Successfully un-registered for notifications", NotifyType.StatusMessage);
                    }
                    else
                    {
                        rootPage.NotifyUser($"Error un-registering for notifications: {result}", NotifyType.ErrorMessage);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    // This usually happens when a device reports that it support notify, but it actually doesn't.
                    rootPage.NotifyUser(ex.Message, NotifyType.ErrorMessage);
                }
            }
        }

        private async void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            // BT_Code: An Indicate or Notify reported that the value has changed.
            // Display the new value with a timestamp.
            var newValue = FormatValueByPresentation(args.CharacteristicValue, presentationFormat);
            var message = $"Value at {DateTime.Now:hh:mm:ss.FFF}: {newValue}";
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
                () => CharacteristicLatestValue.Text = message);
        }

        private string FormatValueByPresentation(IBuffer buffer, GattPresentationFormat format)
        {
            // BT_Code: For the purpose of this sample, this function converts only UInt32 and
            // UTF-8 buffers to readable text. It can be extended to support other formats if your app needs them.

            //JGW ##################################################################################################
            //4.30.20 TODO
            //pass data to keyboard manager
            TimedKeyboardManager.evaluateBLEdata(buffer);


            /*
             *
             * 
           if (data == null)
           {
               //to nothing
           }

           if (data != null)
           {
              

               try
               {
                   char dataChar = (char)data[0];
                   //double dataDouble = Char.GetNumericValue(dataChar);
                   double dataDouble = (double)dataChar;
                   Debug.WriteLine("got input"); //dataDouble.ToString()
                   if (dataDouble >= 0 && dataDouble <=100) //NOTE this presume we're working with a 0-10 range of data
                   {

                       if (dataDouble > 18)
                       {
                           Debug.WriteLine("trigger now");
                           TimedKeyboardManager.newestTimeSesorAboveThreshold = TimedKeyboardManager.globalStopwatch.ElapsedMilliseconds;   //uncomment this line to return input injecion

                       }
                       return dataDouble.ToString();
                   }

               }
               catch {
               }

           }*/

            //jgw: below is old code from example used for this method

            /*
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);

            if (format != null)
            {
                if (format.FormatType == GattPresentationFormatTypes.UInt32 && data.Length >= 4)
                {
                    Int32 XyzMagReceived=BitConverter.ToInt32(data, 0);
                    if (XyzMagReceived>40)
                    {
                       //StrikeUp(); //this function called to send keyboard strikes. May need to modify timing and key release
                    }
                    return BitConverter.ToInt32(data, 0).ToString();
                }
                else if (format.FormatType == GattPresentationFormatTypes.Utf8)
                {
                    try
                    {
                        return Encoding.UTF8.GetString(data);
                    }
                    catch (ArgumentException)
                    {
                        return "(error: Invalid UTF-8 string)";
                    }
                }
                else
                {
                    // Add support for other format types as needed.
                    return "Unsupported format: " + CryptographicBuffer.EncodeToHexString(buffer);
                }
            }

            else
            {
               
                return "Empty data received";
            }
            */

            //jgw this below is redudant but helpful for display what info is received.Method needs to return a string anyway
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);
            char dataChar = (char)data[0];
            double dataDouble = (double)dataChar;
            //Debug.WriteLine("got input"); //dataDouble.ToString()
            return dataDouble.ToString();
        }
        
    }
}
