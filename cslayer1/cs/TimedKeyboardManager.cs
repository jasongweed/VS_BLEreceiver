using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Windows.UI.Input.Preview.Injection;
using Windows.UI.Xaml;
using Windows.Gaming.Input;
using System.Net.Http;
using Windows.Storage.Streams;
using Windows.Security.Cryptography;

namespace SDKTemplate
{
    static class TimedKeyboardManager
    {

        #region Virtual Keyboard
        private static InputInjector inputInjector = InputInjector.TryCreate();
        static List<InjectedInputKeyboardInfo> vKeyBoardInfoList = new List<InjectedInputKeyboardInfo>();
        private static InjectedInputKeyboardInfo vKeyBoardInfo_Press = new InjectedInputKeyboardInfo();
        private static InjectedInputKeyboardInfo vKeyBoardInfo_Release = new InjectedInputKeyboardInfo();
        #endregion

        #region Virtual Gamepad
        //static List<InjectedInputGamepadInfo> vGamepadInfoList = new List<InjectedInputGamepadInfo>();
        //private static InjectedInputGamepadInfo vGamepadInfo_Press = new InjectedInputGamepadInfo();
        //private static InjectedInputGamepadInfo vGamepadInfo_Release = new InjectedInputGamepadInfo();
        #endregion

        #region Timekeeping variables
        public static Stopwatch globalStopwatch = new Stopwatch();
        public static long releaseAccelerateKeyTime = 0;
        public static long newestTimeSesorAboveThreshold = 0;
        public static long previousTimeSesorAboveThreshold = 0;
        public static long lastKeyPressTime = 0;
        public static long millisToHoldKey = 600;
        public static long elapsedFrozen = 0; //for testing purposes
        private static bool keyPressed = false;
        //private long elapsedTime = 0; //maybe not necesssary
        #endregion

        public static void Start()
        {
            globalStopwatch.Start();
            PrepareVirtualKeyboardInput();
            //attempt at gamepad
            //PrepareGamepadInput();
            StartSensorKeyboardLoopTask();// jgw 4/30/20 commented out to make faster (?)
            Debug.WriteLine("starting Keyboard Manager");
        }

        public static void evaluateBLEdata(IBuffer buffer)
        //this takes a data packet and modifies the variable newestTimeSensorAboveThreshold (shared between main thread and looping task), 
        //which the StartSensorKeyboardLoopTask references to determine when to release the button press
        {
            byte[] data;
            CryptographicBuffer.CopyToByteArray(buffer, out data);

            if (data == null)
            {
                //do nothing
            }

            if (data != null)
            {


                try
                {
                    char dataChar = (char)data[0];
                    double dataDouble = (double)dataChar;
                    Debug.WriteLine("got input"); //dataDouble.ToString()
                    if (dataDouble >= 0 && dataDouble <= 100) 
                    {

                        if (dataDouble > 18) //18 is threhold for iphone based sensor as of
                        {
                            Debug.WriteLine("trigger now");
                            TimedKeyboardManager.newestTimeSesorAboveThreshold = TimedKeyboardManager.globalStopwatch.ElapsedMilliseconds;   //uncomment this line to return input injecion

                        }
                        //return dataDouble.ToString();
                    }

                }
                catch
                {
                }
            }
        }

        public static void StartSensorKeyboardLoopTask()
        {
            var t = Task.Run(() => {
                ///start of asynchronous task function definition
                Debug.WriteLine("Executed function from new thread!!");
                ///TOOD: make a while loop
                while (true)
                {
                    //if there's a new reading that crosses threshold, extend time to hold key
                    if(previousTimeSesorAboveThreshold!= newestTimeSesorAboveThreshold)
                    {
                        //set the previous time detected as the newest time detected
                        previousTimeSesorAboveThreshold = newestTimeSesorAboveThreshold;
                        
                        //update the time to release the key
                        releaseAccelerateKeyTime = newestTimeSesorAboveThreshold + millisToHoldKey;
                    }
                    else
                    {
                        //no new update, maybe can block or sleep the thread for a 50ish milliseconds
                    }

                    //**       Manage keyboard controls 

                    elapsedFrozen = globalStopwatch.ElapsedMilliseconds; //for testing
                    //if still in window to hold key, press keyboard button 
                    if (elapsedFrozen < releaseAccelerateKeyTime)
                    {
                        //if (keyPressed == false)
                        {
                            // next line ensures spacing between key presses in time
                            if (elapsedFrozen - lastKeyPressTime > 1)
                            {
                                //press the key to accelerate in game!
                                pressKey();
                                //attempt gamepad
                                //pressGamepadButton();
                                lastKeyPressTime = elapsedFrozen;
                                keyPressed = true;
                            }
                                
                        }
                        
                    }
                    else
                    {
                        //if (keyPressed == true)
                        {
                            //let off the gas!
                            releaseKey(); //multiple of these needed for rocket league
                            //attempt Gamepad
                            //releaseGamePadButton();
                            keyPressed = false;
                        }

                    }

                    Task.Delay(300);
                    

                }//end of loop to update button pressing

            });///end of asynchronous task function definition
        }


        public static void PrepareVirtualKeyboardInput()
        {
            
            //do inputInjector.InjectKeyoboardInput(something of kind InjectedInputKeyboardInfo)

            //press the 'up' key in first variable; default KeyOptions is press
            vKeyBoardInfo_Press.VirtualKey = 0X39;//change to 0x26 for up arrow OR 0X57 for W key on https://msdn.microsoft.com/en-us/library/windows/desktop/dd375731(v=vs.85).aspx
            //vKeyBoardInfo_Press.ScanCode = 48;
            vKeyBoardInfo_Press.KeyOptions+= 0;

            //release the 'up key in second variable; have to specify release
            vKeyBoardInfo_Release.VirtualKey = 0X39; //change to 0x26 for up arrow  https://msdn.microsoft.com/en-us/library/windows/desktop/dd375731(v=vs.85).aspx
            vKeyBoardInfo_Release.KeyOptions+= 2; //changes the enum from 0 to 2, 0 being default press, 2 being release

            //load the sequence of keying events into the list
            //vKeyBoardInfoList.Add(vKeyBoardInfo_Press);
            //vKeyBoardInfoList.Add(vKeyBoardInfo_Release);
            
        }


        public static void PrepareGamepadInput()
        {

            //do inputInjector.InjectKeyoboardInput(something of kind InjectedInputKeyboardInfo)

            //press the 'up' key in first variable; default KeyOptions is press
            
            //vGamepadInfo_Press.Buttons = GamepadButtons.A;//set to 4 for 'A' button   https://docs.microsoft.com/en-us/uwp/api/windows.gaming.input.gamepadbuttons 
            //vKeyBoardInfo_Press.ScanCode = 48;
            //vGamepadInfo_Press.Buttons -= 4; //change to zero to turn off button

            //release the 'up key in second variable; have to specify release
            //vGamepadInfo_Release.Buttons = GamepadButtons.None; //change to 0x26 for up arrow  https://msdn.microsoft.com/en-us/library/windows/desktop/dd375731(v=vs.85).aspx
            //vKeyBoardInfo_Release.KeyOptions += 2; //changes the enum from 0 to 2, 0 being default press, 2 being release
            
        }

        public static void releaseKey()
        {
            vKeyBoardInfoList.Clear();
            vKeyBoardInfoList.Add(vKeyBoardInfo_Release);
            try
            {
                inputInjector.InjectKeyboardInput(vKeyBoardInfoList);
            }
            catch
            {
            }
        }

        public static void releaseGamePadButton()
        {
            try
            {
                //inputInjector.InjectGamepadInput(vGamepadInfo_Release);
            }
            catch{}
        }


        public static void pressKey()
        {
            vKeyBoardInfoList.Clear();
            vKeyBoardInfoList.Add(vKeyBoardInfo_Press);
            try
            {
                inputInjector.InjectKeyboardInput(vKeyBoardInfoList);
            }
            catch{}
            Debug.WriteLine("Press key!");
        }

        public static void pressGamepadButton()
        {
            try
            {
                //inputInjector.InjectGamepadInput(vGamepadInfo_Press);
            }
            catch { }
            Debug.WriteLine("Press key!");
        }

        static public void TestDebugLog()
        {
            Debug.WriteLine("Executed function from SensorGameInputManager class!");
        }

        static public void TestNewThread()
        {
            var t = Task.Run(() => {
                Debug.WriteLine("Executed function from new thread!!");
            });
        }


    } //end of class
}
