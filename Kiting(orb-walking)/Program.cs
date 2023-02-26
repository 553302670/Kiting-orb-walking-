using Kiting_orb_walking_.Modules;
using LowLevelInput.Hooks;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Timers;

namespace Kiting_orb_walking_
{
    public class Program
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private const string ActivePlayerEndpoint = @"https://127.0.0.1:2999/liveclientdata/activeplayer";
        private const string PlayerListEndpoint = @"https://127.0.0.1:2999/liveclientdata/playerlist";
        //private const string GamestatsEndpoint = @"https://127.0.0.1:2999/liveclientdata/gamestats";
        private const string ChampionStatsEndpoint = @"https://raw.communitydragon.org/latest/game/data/characters/";
        private const string SettingsFile = @"settings\settings.json";

        private static bool HasProcess = false;
        private static bool IsExiting = false;
        private static bool IsIntializingValues = false;
        private static bool IsUpdatingAttackValues = false;

        private static readonly Settings CurrentSettings = new Settings();
        private static readonly WebClient Client = new WebClient();
        private static readonly InputManager InputManager = new InputManager();
        private static Process LeagueProcess = null;

        private static readonly Timer OrbWalkTimer = new Timer(100d / 3d);

        private static bool OrbWalkerTimerActive = false;

        private static string ActivePlayerName = string.Empty;
        private static string ChampionName = string.Empty;
        private static string RawChampionName = string.Empty;

        //private static double GameTime = 0;
        private static double ClientAttackSpeed = 0.625;
        private static double ChampionAttackCastTime = 0.625;
        private static double ChampionAttackTotalTime = 0.625;
        private static double ChampionAttackSpeedRatio = 0.625;
        private static double ChampionAttackDelayPercent = 0.3;
        private static double ChampionAttackDelayScaling = 1.0;

    
        // Ping值补偿，延迟高、输入有延迟
        private static double WindupBuffer = 1d / 15d;

        // 攻击频率
        private static readonly double MinInputDelay = 1d / 30d;
        //private static readonly Random rnd = new Random();
        // 时间间隔
        private static readonly double OrderTickRate = 1d / 30d;


        // 计算前摇 计算方式详见https://leagueoflegends.fandom.com/wiki/Basic_attack#Attack_speed
        public static double GetSecondsPerAttack() => 1 / ClientAttackSpeed;
        public static double GetWindupDuration() => (((GetSecondsPerAttack() * ChampionAttackDelayPercent) - ChampionAttackCastTime) * ChampionAttackDelayScaling) + ChampionAttackCastTime;
        public static double GetBufferedWindupDuration() => GetWindupDuration() + WindupBuffer;
        
        // 检测是否存在配置文件，没有就创建然后载入配置
        public static void Main(string[] args)
        {
            if (!File.Exists(SettingsFile))
            {
                Directory.CreateDirectory("settings");
                CurrentSettings.CreateNew(SettingsFile);
            }
            else
            {
                CurrentSettings.Load(SettingsFile);
            }

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            Client.Proxy = null;

            Console.Clear();
            Console.CursorVisible = false;

            Console.Title = "Kiting(orb-walking)";
            
            InputManager.Initialize();
            InputManager.OnKeyboardEvent += InputManager_OnKeyboardEvent;
            InputManager.OnMouseEvent += InputManager_OnMouseEvent;

            OrbWalkTimer.Elapsed += OrbWalkTimer_Elapsed;


            Timer attackSpeedCacheTimer = new Timer(OrderTickRate);
            attackSpeedCacheTimer.Elapsed += AttackSpeedCacheTimer_Elapsed;
            attackSpeedCacheTimer.Start();

            //Ping值补偿
            /* 
            Console.WriteLine("请输入游戏延迟(Ping)：");
            WindupBuffer = 1/double.Parse(Console.ReadLine())*2;
            */
            WindupBuffer = CurrentSettings.Ping/1000d;


            Console.WriteLine($"请按住 '{(VirtualKeyCode)CurrentSettings.ActivationKey}键' 激活走砍");

            CheckLeagueProcess();

            Console.ReadLine();
        }


        private static void InputManager_OnMouseEvent(VirtualKeyCode key, KeyState state, int x, int y)
        {
        }

        private static void InputManager_OnKeyboardEvent(VirtualKeyCode key, KeyState state)
        {
            if (key == (VirtualKeyCode)CurrentSettings.ActivationKey)
            {
                switch (state)
                {
                    case KeyState.Down when !OrbWalkerTimerActive:
                        OrbWalkerTimerActive = true;
                        OrbWalkTimer.Start();
                        break;

                    case KeyState.Up when OrbWalkerTimerActive:
                        OrbWalkerTimerActive = false;
                        OrbWalkTimer.Stop();
                        break;
                }
            }
        }

        // 初始化计时器 DateTime
        /*
        private static double nextInput = default;
        private static double nextMove = default;
        private static double nextAttack = default;
        */
        private static DateTime nextInput = default;
        private static DateTime nextMove = default;
        private static DateTime nextAttack = default;

        private static readonly Stopwatch owStopWatch = new Stopwatch();

        private static void OrbWalkTimer_Elapsed(object sender, ElapsedEventArgs e)
        {

            if (!HasProcess || IsExiting || GetForegroundWindow() != LeagueProcess.MainWindowHandle)
            {

                return;
            }

            // 将计时器开始时的时间存储到变量中
            var time = e.SignalTime;


            // 下次输入计时器时间小于计时器时间运行
            if (true || nextInput < time)
            {
                // 当攻击时间小于计时器时间运行
                if (nextAttack <= time)
                {
                    // 计算下次输入计时器时间 = 当前时间 + 攻击频率
                    nextInput = time.AddSeconds(MinInputDelay);

                    // 攻击操作
                    InputSimulator.Keyboard.KeyDown((ushort)DirectInputKeys.DIK_A);
                    InputSimulator.Mouse.MouseClick(InputSimulator.Mouse.Buttons.Left);
                    InputSimulator.Keyboard.KeyUp((ushort)DirectInputKeys.DIK_A);


                    // 更新攻击计时器时间
                    var attackTime = DateTime.Now;

                    // 计算下次移动时间 = 前摇 + Ping值补偿
                    nextMove = attackTime.AddSeconds(GetBufferedWindupDuration());
                    // 计算下次攻击时间 = 每秒攻击次数
                    if (ChampionName.Equals("复仇之矛"))
                    {
                        nextAttack = attackTime.AddSeconds(GetSecondsPerAttack() - GetBufferedWindupDuration());
                    }
                    else
                    {
                        nextAttack = attackTime.AddSeconds(GetSecondsPerAttack());
                    }
                    
                }
                // 当移动时间小于计时器时间运行GetSecondsPerAttack()
                else if (nextMove < time)
                {
                    // 计算下次输入计时器时间 = 当前时间 + 攻击频率
                    nextInput = time.AddSeconds(MinInputDelay);
                    nextMove = nextMove.AddSeconds(GetSecondsPerAttack()- GetWindupDuration());
                    // 移动操作
                    InputSimulator.Mouse.MouseClick(InputSimulator.Mouse.Buttons.Right);
                    
                    
                }
            }

        }

        //检测联盟是否在运行
        private static void CheckLeagueProcess()
        {
            while (LeagueProcess is null || !HasProcess)
            {
                LeagueProcess = Process.GetProcessesByName("League of Legends").FirstOrDefault();
                if (LeagueProcess is null || LeagueProcess.HasExited)
                {
                    continue;
                }
                HasProcess = true;
                LeagueProcess.EnableRaisingEvents = true;
                LeagueProcess.Exited += LeagueProcess_Exited;
            }
        }

        private static void LeagueProcess_Exited(object sender, EventArgs e)
        {
            HasProcess = false;
            LeagueProcess = null;
            //Console.Clear();
            ChampionName = string.Empty;
            IsIntializingValues = false;
            IsUpdatingAttackValues = false;
            
            Console.WriteLine("League Process Exited");
            CheckLeagueProcess();
        }

        private static void AttackSpeedCacheTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (HasProcess && !IsExiting && !IsIntializingValues && !IsUpdatingAttackValues)
            {
                IsUpdatingAttackValues = true;

                JToken activePlayerToken = null;
                //JToken gamestatsToken = null;
                try
                {
                    activePlayerToken = JToken.Parse(Client.DownloadString(ActivePlayerEndpoint));
                   // gamestatsToken = JToken.Parse(Client.DownloadString(GamestatsEndpoint));
                }
                catch
                {
                    IsUpdatingAttackValues = false;
                    return;
                }

                if (string.IsNullOrEmpty(ChampionName))
                {
                    ActivePlayerName = activePlayerToken?["summonerName"].ToString();
                    IsIntializingValues = true;
                    JToken playerListToken = JToken.Parse(Client.DownloadString(PlayerListEndpoint));
                    foreach (JToken token in playerListToken)
                    {
                        if (token["summonerName"].ToString().Equals(ActivePlayerName))
                        {
                            ChampionName = token["championName"].ToString();
                            string[] rawNameArray = token["rawChampionName"].ToString().Split('_', StringSplitOptions.RemoveEmptyEntries);
                            RawChampionName = rawNameArray[^1];
                        }
                    }
                    
                    if (!GetChampionBaseValues(RawChampionName))
                    {
                        IsIntializingValues = false;
                        IsUpdatingAttackValues = false;
                        return; 
                    }


                    Console.Title = $"({ActivePlayerName}) {ChampionName}";


                    IsIntializingValues = false;
                }

                
                Console.SetCursorPosition(0, 2);
                Console.WriteLine($"当前攻速: {ClientAttackSpeed:0.00}\n" +
                    $"攻击速率: {GetSecondsPerAttack():0.00}s\n" +
                    $"前摇修正: {GetWindupDuration():0.00}s + {WindupBuffer:0.00}s delay\n" +
                    $"攻击后摇: {(GetSecondsPerAttack() - GetWindupDuration()):0.00}s\n");


               // GameTime = gamestatsToken["gameTime"].Value<double>() * 1000d;
                ClientAttackSpeed = activePlayerToken["championStats"]["attackSpeed"].Value<double>();
                IsUpdatingAttackValues = false;
            }
        }

        
        private static bool GetChampionBaseValues(string championName)
        {
            string lowerChampionName = championName.ToLower();
            JToken championBinToken = null;
            try
            {
                championBinToken = JToken.Parse(Client.DownloadString($"{ChampionStatsEndpoint}{lowerChampionName}/{lowerChampionName}.bin.json"));
            }
            catch
            {
                return false;
            }
            JToken championRootStats = championBinToken[$"Characters/{championName}/CharacterRecords/Root"];
            ChampionAttackSpeedRatio = championRootStats["attackSpeedRatio"].Value<double>(); ;

            JToken championBasicAttackInfoToken = championRootStats["basicAttack"];
            JToken championAttackDelayOffsetToken = championBasicAttackInfoToken["mAttackDelayCastOffsetPercent"];
            JToken championAttackDelayOffsetSpeedRatioToken = championBasicAttackInfoToken["mAttackDelayCastOffsetPercentAttackSpeedRatio"];

            if (championAttackDelayOffsetSpeedRatioToken?.Value<double?>() != null)
            {
                ChampionAttackDelayScaling = championAttackDelayOffsetSpeedRatioToken.Value<double>();
            }

            if (championAttackDelayOffsetToken?.Value<double?>() == null)
            {
                JToken attackTotalTimeToken = championBasicAttackInfoToken["mAttackTotalTime"];
                JToken attackCastTimeToken = championBasicAttackInfoToken["mAttackCastTime"];

                if (attackTotalTimeToken?.Value<double?>() == null && attackCastTimeToken?.Value<double?>() == null)
                {
                    string attackName = championBasicAttackInfoToken["mAttackName"].ToString();
                    string attackSpell = $"Characters/{attackName.Split(new[] { "BasicAttack" }, StringSplitOptions.RemoveEmptyEntries)[0]}/Spells/{attackName}";
                    ChampionAttackDelayPercent += championBinToken[attackSpell]["mSpell"]["delayCastOffsetPercent"].Value<double>();
                }
                else
                {
                    ChampionAttackTotalTime = attackTotalTimeToken.Value<double>();
                    ChampionAttackCastTime = attackCastTimeToken.Value<double>(); ;

                    ChampionAttackDelayPercent = ChampionAttackCastTime / ChampionAttackTotalTime;
                }
            }
            else
            {
                ChampionAttackDelayPercent += championAttackDelayOffsetToken.Value<double>(); ;
            }

            return true;
        }
    }
}
