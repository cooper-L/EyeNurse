﻿using Caliburn.Micro;
using DZY.Util.Common.Helpers;
using DZY.Util.WPF.ViewModels;
using DZY.Util.WPF.Views;
using DZY.WinAPI.Helpers;
using EyeNurse.Client.Configs;
using EyeNurse.Client.Events;
using EyeNurse.Client.ViewModels;
using EyeNurse.Client.Views;
using Hardcodet.Wpf.TaskbarNotification;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Windows.Media.SpeechSynthesis;

namespace EyeNurse.Client.Services
{
    public class EyeNurseService : INotifyPropertyChanged, IHandle<AppSettingChangedEvent>
    {
        private Logger logger = NLog.LogManager.GetCurrentClassLogger();
        Timer _timer;
        IWindowManager _windowManager;
        LockScreenViewModel _lastLockScreenViewModel;

        TaskbarIcon _taskbarIcon;
        Icon _sourceIcon;
        readonly IEventAggregator _eventAggregator;

        bool warned;
        private PurchaseTipsViewModel _tipsVM;
        private IntPtr _mainHandler;
        private TimeSpan _totalPlayTime = new TimeSpan();
        //private int _currentPID;

        public EyeNurseService(IWindowManager windowManager, IEventAggregator eventAggregator)
        {
            //_currentPID = Process.GetCurrentProcess().Id;
            _eventAggregator = eventAggregator;
            _eventAggregator.Subscribe(this);

            _windowManager = windowManager;

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            ConfigFilePath = $"{appData}\\EyeNurse\\Configs\\setting.json";
            AppDataFilePath = $"{appData}\\EyeNurse\\appData.json";

            _timer = new Timer
            {
                Interval = 1000
            };
            _timer.Elapsed += Timer_Elapsed;

            //读取 Setting
            ReloadSetting();

            //读取AppData
            ReloadAppData();
        }

        #region private methods

        private void ResetCountDown()
        {
            //休息结束
            if (_lastLockScreenViewModel != null)
            {
                _lastLockScreenViewModel.TryClose();
                _lastLockScreenViewModel = null;
            }

            _timer.Stop();
            IsResting = false;

            Countdown = Setting.App.AlarmInterval;
            CountdownPercent = 100;

            _timer.Start();
        }

        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (IsPaused)
            {
                PausedTime = PausedTime.Add(TimeSpan.FromMilliseconds(_timer.Interval));
                if (_taskbarIcon == null)
                {
                    _taskbarIcon = IoC.Get<TaskbarIcon>();
                    _sourceIcon = _taskbarIcon.Icon;
                }
            }
            else
            {
                PausedTime = new TimeSpan();
                //游戏中，延迟
                if (IsDelaying)
                {
                    warned = false;
                    IsDelaying = false;
                    ResetCountDown();
                }
                //正常运行中
                else if (!IsResting)
                {
                    //倒计时减1秒
                    Countdown = Countdown.Subtract(new TimeSpan(0, 0, 1));
                    CountdownPercent = Countdown.TotalSeconds / Setting.App.AlarmInterval.TotalSeconds * 100;
                    //提前30秒警告一次
                    if (!warned && Countdown.TotalSeconds <= 30 && Countdown.TotalSeconds >= 20)
                    {
                        warned = true;
                        ////游戏中不播放警告声音
                        //bool isMaximized = new OtherProgramChecker(_currentPID, true).CheckMaximized();
                        //if (!isMaximized)
                        //还是加上提示，害怕突然说话过于惊悚
                        _eventAggregator.PublishOnUIThread(new PlayAudioEvent()
                        {
                            Source = @"Resources\Sounds\breakpre.mp3"
                        });
                    }
                    //判断休息
                    if (Countdown.TotalSeconds <= 0)
                    {
                        _timer.Stop();

                        new OtherProgramChecker(Process.GetCurrentProcess().Id, true).CheckMaximized(out List<System.Windows.Forms.Screen> maximizedScreens);
                        bool isMaximized = maximizedScreens != null && maximizedScreens.Count > 0;
                        // 正在全屏&&开启语音提示
                        if ((isMaximized && Setting.Speech.Enable))
                        {
                            IsDelaying = true;
                            PlaySpeech();
                        }
                        else
                        {//没有全屏玩游戏，立即休息
                            IsResting = true;

                            //开启不锁屏
                            if (Setting.Speech.NeverLockScreen)
                            {
                                PlaySpeech();
                            }
                            else
                            {//锁屏
                                _lastLockScreenViewModel = IoC.Get<LockScreenViewModel>();
                                Execute.OnUIThread(() =>
                                {
                                    _windowManager.ShowWindow(_lastLockScreenViewModel);
                                    _lastLockScreenViewModel.Deactivated += _lastLockScreenViewModel_Deactivated;
                                });
                            }
                            PlayRestingAudio(IsResting);
                        }

                        RestTimeCountdown = Setting.App.RestTime;

                        _timer.Start();
                    }
                }
                //休息中
                else
                {
                    warned = false;
                    RestTimeCountdown = RestTimeCountdown.Subtract(new TimeSpan(0, 0, 1));
                    RestTimeCountdownPercent = RestTimeCountdown.TotalSeconds / Setting.App.RestTime.TotalSeconds * 100;
                    if (RestTimeCountdown.TotalSeconds <= 0)
                    {
                        //休息完毕
                        ResetCountDown();
                        PlayRestingAudio(IsResting);
                        _totalPlayTime = new TimeSpan();
                    }
                }
            }
        }

        private async void PlaySpeech()
        {
            try
            {
                _totalPlayTime = _totalPlayTime.Add(Setting.App.AlarmInterval);
                //todo 
                //using (SpeechSynthesizer syn = new SpeechSynthesizer())
                //{
                //    string setting = Setting.Speech.Message;
                //    if (string.IsNullOrEmpty(setting))
                //        setting = SpeechSetting.DefaultMessage;

                //    string hourMsg = "";
                //    if (_totalPlayTime.Hours > 0)
                //        hourMsg = $"{_totalPlayTime.Hours}小时 {_totalPlayTime.Minutes}分";
                //    else
                //        hourMsg = $"{_totalPlayTime.Minutes}分钟";

                //    string msg = string.Format(setting, hourMsg);
                //    syn.Speak(msg);
                //}

                string setting = Setting.Speech.Message;
                string hourMsg = "";
                if (_totalPlayTime.Hours > 0)
                    hourMsg = $"{_totalPlayTime.Hours}小时 {_totalPlayTime.Minutes}分";
                else
                    hourMsg = $"{_totalPlayTime.Minutes}分钟";

                string msg = string.Format(setting, hourMsg);

                using var synthesizer = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
                using Windows.Media.SpeechSynthesis.SpeechSynthesisStream synthStream = await synthesizer.SynthesizeTextToStreamAsync(msg);
                using Stream stream = synthStream.AsStreamForRead();
                using System.Media.SoundPlayer player = new System.Media.SoundPlayer();
                player.Stream = stream;
                player.Play();
                var icon = IoC.Get<TaskbarIcon>();
                icon.ShowBalloonTip("休息提示", msg, BalloonIcon.Info);
            }
            catch (Exception ex)
            {
                logger.Error($"PlaySpeech Ex:{ex.Message}");
            }
        }

        private void _tipsVM_Deactivated(object sender, EventArgs e)
        {
            var temp = sender as PurchaseTipsViewModel;
            temp.Deactivated -= _tipsVM_Deactivated;

            AppData.Purchased = _tipsVM.Purchased || AppData.Purchased;
            _eventAggregator.PublishOnBackgroundThread(new VipEvent() { IsVIP = AppData.Purchased });

            AppData.Reviewed = _tipsVM.Rated || AppData.Reviewed;
            SaveAppData();

            _tipsVM = null;

            if (semaphoreSlim.CurrentCount == 0)
                semaphoreSlim.Release();
        }

        private void _lastLockScreenViewModel_Deactivated(object sender, DeactivationEventArgs e)
        {
            var temp = sender as LockScreenViewModel;
            temp.Deactivated -= _lastLockScreenViewModel_Deactivated;
            RestTimeCountdown = new TimeSpan();
        }

        private void PlayRestingAudio(bool resting)
        {
            if (resting)
                _eventAggregator.PublishOnUIThread(new PlayAudioEvent()
                {
                    Source = @"Resources\Sounds\break.mp3"
                });
            else
                _eventAggregator.PublishOnUIThread(new PlayAudioEvent()
                {
                    Source = @"Resources\Sounds\unlock.mp3"
                });
        }

        #endregion

        #region public methods

        public void OpenVIPQQGroupLink()
        {
            try
            {
                Process.Start("https://shang.qq.com/wpa/qunwpa?idkey=24010e6212fe3c7ba6f79f5f91e6b216c6708d7a47abceb6f7e26890c3b15944");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "OpenVIPQQGroupLink Ex");
            }
        }

        public void OpenQQGroupLink()
        {
            try
            {
                Process.Start("https://shang.qq.com/wpa/qunwpa?idkey=e8d8e46fa4067c16110376db53d51065bdce6abb943e08f09736317527bfbf45");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "OpenQQGroupLink Ex");
            }
        }

        public void CopyVipContent()
        {
            try
            {
                Clipboard.SetText(VIPGroup);
            }
            catch (Exception ex)
            {
                MessageBox.Show("拷贝失败，请手动拷贝");
                logger.Error(ex, "CopyVipContent Ex");
            }
        }

        public void ActionUI(object ui)
        {
            if (ui is Window window)
                window.Activate();
        }

        System.Threading.SemaphoreSlim semaphoreSlim = new System.Threading.SemaphoreSlim(0, 1);
        public async Task ShowPurchaseTip()
        {
            if (!Initialized)
                return;

            if (_tipsVM != null)
            {
                ActionUI(_tipsVM.GetView());
                return;
            }

            _tipsVM = new PurchaseTipsViewModel
            {
                BGM = new Uri("Resources//Sounds//PurchaseTipsBg.mp3", UriKind.RelativeOrAbsolute),
                Content = new DefaultPurchaseTipsContent(),
                PurchaseContent = "真可怜，给他买个包子吧",
                RatingContent = "造孽啊，给个精神抚慰吧",
                //CancelContent = "不管，饿死算球"
            };

            //StoreHelper store = new StoreHelper(_mainHandler);
            _tipsVM.Initlize(GetPurchaseViewModel());
            //_tipsVM.DisplayName = "Duang Duang Duang ! ! !";
            _tipsVM.Deactivated += _tipsVM_Deactivated;

            dynamic setting = new ExpandoObject();
            setting.Width = 800;
            setting.Height = 450;
            setting.ResizeMode = ResizeMode.NoResize;
            setting.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            _windowManager.ShowWindow(_tipsVM, null, setting);
            await semaphoreSlim.WaitAsync();
        }

        public async Task Init(IntPtr mainHandler)
        {
            if (Initialized || IsInitializing)
                return;

            _mainHandler = mainHandler;

            Initialized = false;
            IsInitializing = true;

            CheckUpates();

            //_eventAggregator.PublishOnBackgroundThread(new ServiceInitEvent() { Initialized = Initialized, IsInitializing = IsInitializing });

            ResetCountDown();

            var vm = GetPurchaseViewModel();
            await vm.LoadProducts();//从服务端获取vip状态

            CheckVIP(vm);

            if (AppData.LastTipsDate == new DateTime())
                AppData.LastTipsDate = DateTime.Now;
            var ts = DateTime.Now - AppData.LastTipsDate;

            bool showTips = false;

            if (!AppData.Purchased)
            {
                if (!AppData.Reviewed && ts.TotalDays >= 5)
                    showTips = true;
                else if (AppData.Reviewed && ts.TotalDays >= 10)
                    showTips = true;
            }

            if (showTips)
            {
                AppData.LastTipsDate = DateTime.Now;
                SaveAppData();
                await ShowPurchaseTip();
            }

            Initialized = true;
            IsInitializing = false;
            //_eventAggregator.PublishOnBackgroundThread(new ServiceInitEvent() { Initialized = Initialized, IsInitializing = IsInitializing });
        }

        private async void CheckUpates()
        {
            StoreHelper store = new StoreHelper(_mainHandler);
            var icon = IoC.Get<TaskbarIcon>();

            await store.DownloadAndInstallAllUpdatesAsync(() =>
             {
                 var result = MessageBox.Show("是否更新。", "检测到新版本", MessageBoxButton.OKCancel);
                 return result == MessageBoxResult.OK;
             }, (progress) =>
             {
                 if ((int)progress.PackageUpdateState >= 3)
                     icon.ShowBalloonTip("温馨提示", $"《眼睛护士》如果更新失败，请关闭软件打开应用商店手动更新。", BalloonIcon.Info);
             });
        }

        public async void Purchase()
        {
            var vm = GetPurchaseViewModel();
            //不用等待加载完成
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            vm.LoadProducts();
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            _windowManager.ShowDialog(vm);

            //直接关窗口，在检查一次
            if (!vm.PurchaseResult)
                await vm.LoadProducts();
            CheckVIP(vm);
        }

        public void CheckVIP(PurchaseViewModel vm)
        {
            try
            {
                if (AppData.Purchased != vm.IsVIP)
                {
                    AppData.Purchased = vm.IsVIP;
                    SaveAppData();
                }
                _eventAggregator.PublishOnBackgroundThread(new VipEvent() { IsVIP = vm.IsVIP });
            }
            catch (Exception ex)
            {
                logger.Warn("CheckVIP EX:" + ex);
            }
        }

        public void ReloadAppData()
        {
            AppData = JsonHelper.JsonDeserializeFromFile<AppData>(AppDataFilePath);
            if (AppData == null)
            {
                AppData = new AppData();
            }
            SaveAppData();
        }

        public void SaveAppData()
        {
            JsonHelper.JsonSerialize(AppData, AppDataFilePath);
        }

        public void ReloadSetting()
        {
            Setting = JsonHelper.JsonDeserializeFromFile<Setting>(ConfigFilePath);
            bool save = false;
            if (Setting == null || Setting.App == null)
            {
                //默认值
                Setting = new Setting();
                save = true;
            }

            if (Setting.Speech == null)
            {
                Setting.Speech = new SpeechSetting();
                save = true;
            }

            if (save)
                JsonHelper.JsonSerialize(Setting, ConfigFilePath);
        }

        readonly string VIPGroup = "864039359";
        public PurchaseViewModel GetPurchaseViewModel()
        {
            var vm = new PurchaseViewModel();
            vm.InitHandle(_mainHandler, Dispatcher.CurrentDispatcher);
            vm.Initlize(new string[] { "Durable" }, new string[] { "9P3F93X9QJRV", "9PM5NZ2V9D6S", "9P98QTMNM1VZ" });
            //vm.VIPContent = new VIPContent($"巨应工作室VIP QQ群：{VIPGroup}", VIPGroup, "https://shang.qq.com/wpa/qunwpa?idkey=24010e6212fe3c7ba6f79f5f91e6b216c6708d7a47abceb6f7e26890c3b15944");

            vm.VIPContent = new Label() { Content = "购买成功，将不再有提示，感谢支持!" };
            return vm;
        }

        public async void Handle(AppSettingChangedEvent message)
        {
            Setting = await JsonHelper.JsonDeserializeFromFileAsync<Setting>(ConfigFilePath);
        }

        #endregion

        #region properties

        #region Initialized

        /// <summary>
        /// The <see cref="Initialized" /> property's name.
        /// </summary>
        public const string InitializedPropertyName = "Initialized";

        private bool _Initialized;

        /// <summary>
        /// Initialized
        /// </summary>
        public bool Initialized
        {
            get { return _Initialized; }

            set
            {
                if (_Initialized == value) return;

                _Initialized = value;
                NotifyOfPropertyChange(InitializedPropertyName);
            }
        }

        #endregion

        #region IsInitializing

        /// <summary>
        /// The <see cref="IsInitializing" /> property's name.
        /// </summary>
        public const string IsInitializingPropertyName = "IsInitializing";

        private bool _IsInitializing;

        /// <summary>
        /// IsInitializing
        /// </summary>
        public bool IsInitializing
        {
            get { return _IsInitializing; }

            set
            {
                if (_IsInitializing == value) return;

                _IsInitializing = value;
                NotifyOfPropertyChange(IsInitializingPropertyName);
            }
        }

        #endregion

        #region IsResting

        /// <summary>
        /// The <see cref="IsResting" /> property's name.
        /// </summary>
        public const string IsRestingPropertyName = "IsResting";

        private bool _IsResting;

        /// <summary>
        /// IsResting
        /// </summary>
        public bool IsResting
        {
            get { return _IsResting; }

            set
            {
                if (_IsResting == value) return;

                _IsResting = value;


                NotifyOfPropertyChange(IsRestingPropertyName);
            }
        }

        #endregion

        #region IsDelaying

        /// <summary>
        /// The <see cref="IsDelaying" /> property's name.
        /// </summary>
        public const string IsDelayingPropertyName = "IsDelaying";

        private bool _IsDelaying;

        /// <summary>
        /// 全屏，延迟中
        /// </summary>
        public bool IsDelaying
        {
            get { return _IsDelaying; }

            set
            {
                if (_IsDelaying == value) return;

                _IsDelaying = value;
                NotifyOfPropertyChange(IsDelayingPropertyName);
            }
        }

        #endregion

        #region Countdown

        /// <summary>
        /// The <see cref="Countdown" /> property's name.
        /// </summary>
        public const string CountdownPropertyName = "Countdown";

        private TimeSpan _Countdown;

        /// <summary>
        /// Countdown
        /// </summary>
        public TimeSpan Countdown
        {
            get { return _Countdown; }

            set
            {
                if (_Countdown == value) return;

                _Countdown = value;
                NotifyOfPropertyChange(CountdownPropertyName);
            }
        }

        #endregion

        #region CountdownPercent

        /// <summary>
        /// The <see cref="CountdownPercent" /> property's name.
        /// </summary>
        public const string CountdownPercentPropertyName = "CountdownPercent";

        private double _CountdownPercent = 100;

        /// <summary>
        /// CountdownPercent
        /// </summary>
        public double CountdownPercent
        {
            get { return _CountdownPercent; }

            set
            {
                if (_CountdownPercent == value) return;

                _CountdownPercent = value;
                NotifyOfPropertyChange(CountdownPercentPropertyName);
            }
        }

        #endregion

        #region RestTimeCountdown

        /// <summary>
        /// The <see cref="RestTimeCountdown" /> property's name.
        /// </summary>
        public const string RestTimeCountdownPropertyName = "RestTimeCountdown";

        private TimeSpan _RestTimeCountdown;

        /// <summary>
        /// RestTimeCountdown
        /// </summary>
        public TimeSpan RestTimeCountdown
        {
            get { return _RestTimeCountdown; }

            set
            {
                if (_RestTimeCountdown == value) return;

                _RestTimeCountdown = value;
                NotifyOfPropertyChange(RestTimeCountdownPropertyName);
            }
        }

        #endregion

        #region RestTimeCountdownPercent

        /// <summary>
        /// The <see cref="RestTimeCountdownPercent" /> property's name.
        /// </summary>
        public const string RestTimeCountdownPercentPropertyName = "RestTimeCountdownPercent";

        private double _RestTimeCountdownPercent = 100;

        /// <summary>
        /// RestTimeCountdownPercent
        /// </summary>
        public double RestTimeCountdownPercent
        {
            get { return _RestTimeCountdownPercent; }

            set
            {
                if (_RestTimeCountdownPercent == value) return;

                _RestTimeCountdownPercent = value;
                NotifyOfPropertyChange(RestTimeCountdownPercentPropertyName);
            }
        }

        #endregion

        #region IsPaused

        /// <summary>
        /// The <see cref="IsPaused" /> property's name.
        /// </summary>
        public const string IsPausedPropertyName = "IsPaused";

        private bool _IsPaused;

        /// <summary>
        /// IsPaused
        /// </summary>
        public bool IsPaused
        {
            get { return _IsPaused; }

            set
            {
                if (_IsPaused == value) return;

                _IsPaused = value;
                NotifyOfPropertyChange(IsPausedPropertyName);
            }
        }

        #endregion

        #region PausedTime

        /// <summary>
        /// The <see cref="PausedTime" /> property's name.
        /// </summary>
        public const string PausedTimePropertyName = "PausedTime";

        private TimeSpan _PausedTime;

        /// <summary>
        /// PausedTime
        /// </summary>
        public TimeSpan PausedTime
        {
            get { return _PausedTime; }

            set
            {
                if (_PausedTime == value) return;

                _PausedTime = value;
                NotifyOfPropertyChange(PausedTimePropertyName);
            }
        }

        #endregion

        #endregion

        #region public methods

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public bool Pause()
        {
            if (IsResting)
                return false;
            IsPaused = true;
            return true;
        }

        public void Resum()
        {
            IsPaused = false;
        }

        public void Reset()
        {
            ResetCountDown();
        }

        public void RestImmediately()
        {
            if (IsResting)
                return;
            Countdown = new TimeSpan(0, 0, 1);
        }

        #region config

        public string ConfigFilePath { get; private set; }
        public string AppDataFilePath { get; private set; }
        public AppData AppData { get; private set; }
        public Setting Setting { get; private set; }

        #endregion

        #endregion

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        protected void NotifyOfPropertyChange(string propertyName)
        {
            var handle = PropertyChanged;
            if (handle == null)
                return;
            handle(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion

    }
}
