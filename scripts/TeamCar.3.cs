using System;
using System.Globalization;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

public sealed class TeamCar : Script
{
    private const string ConfigPath = "scripts\\TeamCar.ini";

    private Vehicle _car;
    private Ped _driver;
    private Blip _blip;
    private Prop _serviceProp;
    private Keys _toggleKey = Keys.F10;
    private Keys _supportKey = Keys.E;
    private string _vehicleModel = "stratum";
    private string _driverModel = "s_m_m_autoshop_01";
    private string _plate = "TEAM CAR";
    private int _primaryColour = 3;
    private int _secondaryColour = 111;
    private int _surfboardRoofExtra = 1;
    private int _bicycleRoofExtra = 2;
    private float _followDistance = 12.0f;
    private float _cruiseSpeed = 18.0f;
    private float _maximumSpeed = 32.0f;
    private float _speedBuffer = 5.0f;
    private int _drivingStyle = 786603;
    private int _retaskInterval = 5000;
    private bool _respawnWhenLost = true;
    private float _lostDistance = 180.0f;
    private int _stuckMilliseconds = 8000;
    private float _supportRange = 4.5f;
    private float _cyclingSupportRange = 20.0f;
    private float _repairRange = 8.0f;
    private int _supplyCooldownMilliseconds = 10000;
    private int _waterHealth = 5;
    private int _firstAidHealth = 50;
    private bool _serviceAnimations = true;
    private bool _gtBikeVProtection = true;
    private float _gtBikeVMaximumDistance = 90.0f;

    private int _nextRetaskAt;
    private int _nextRespawnAt;
    private int _servicePropDeleteAt;
    private int _stuckSince;
    private bool _recovering;
    private bool _teamCarRequested;
    private bool _supportMenuOpen;
    private int _supportMenuIndex;
    private int _nextSupplyAt;

    private static readonly string[] SupportItems =
    {
        "Water bottle",
        "Energy gel",
        "First aid",
        "Repair nearby bicycle"
    };

    public TeamCar()
    {
        Interval = 50;
        LoadConfiguration();
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Aborted += OnAborted;
    }

    private void LoadConfiguration()
    {
        try
        {
            ScriptSettings settings = ScriptSettings.Load(ConfigPath);
            _toggleKey = settings.GetValue("Controls", "ToggleKey", Keys.F10);
            _supportKey = settings.GetValue("Controls", "SupportKey", Keys.E);
            _vehicleModel = settings.GetValue("Vehicle", "Model", "stratum");
            _driverModel = settings.GetValue("Vehicle", "DriverModel", "s_m_m_autoshop_01");
            _primaryColour = settings.GetValue("Vehicle", "PrimaryColour", 3);
            _secondaryColour = settings.GetValue("Vehicle", "SecondaryColour", 111);
            _plate = settings.GetValue("Vehicle", "Plate", "TEAM CAR");
            _surfboardRoofExtra = settings.GetValue("Vehicle", "SurfboardRoofExtra", 1);
            _bicycleRoofExtra = settings.GetValue("Vehicle", "BicycleRoofExtra", 2);
            _followDistance = Math.Max(6.0f, settings.GetValue("Following", "FollowDistance", 12.0f));
            _cruiseSpeed = Math.Max(4.0f, settings.GetValue("Following", "CruiseSpeed", 18.0f));
            _maximumSpeed = Math.Max(_cruiseSpeed, settings.GetValue("Following", "MaximumSpeed", 32.0f));
            _speedBuffer = Math.Max(1.0f, settings.GetValue("Following", "SpeedBuffer", 5.0f));
            _drivingStyle = settings.GetValue("Following", "DrivingStyle", 786603);
            _retaskInterval = Math.Max(2000, settings.GetValue("Following", "RetaskIntervalMs", 5000));
            _respawnWhenLost = settings.GetValue("Recovery", "RespawnWhenLost", true);
            _lostDistance = Math.Max(60.0f, settings.GetValue("Recovery", "LostDistance", 180.0f));
            _stuckMilliseconds = Math.Max(3000, settings.GetValue("Recovery", "StuckSeconds", 8)) * 1000;
            _supportRange = Math.Max(2.0f, settings.GetValue("Support", "InteractionRange", 4.5f));
            _cyclingSupportRange =
                Math.Max(_supportRange, settings.GetValue("Support", "CyclingInteractionRange", 20.0f));
            _repairRange = Math.Max(_supportRange, settings.GetValue("Support", "BicycleRepairRange", 8.0f));
            _supplyCooldownMilliseconds =
                Math.Max(0, settings.GetValue("Support", "SupplyCooldownSeconds", 10)) * 1000;
            _waterHealth = Math.Max(0, settings.GetValue("Support", "WaterHealth", 5));
            _firstAidHealth = Math.Max(1, settings.GetValue("Support", "FirstAidHealth", 50));
            _serviceAnimations = settings.GetValue("Support", "EnableAnimations", true);
            _gtBikeVProtection = settings.GetValue("Compatibility", "GTBikeVProtection", true);
            _gtBikeVMaximumDistance = Math.Max(
                40.0f,
                Math.Min(95.0f, settings.GetValue("Compatibility", "GTBikeVMaximumDistance", 90.0f)));
        }
        catch (Exception exception)
        {
            Notification.Show("~r~Team Car config error:~s~ " + exception.Message);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_supportMenuOpen)
        {
            HandleSupportMenuKey(e.KeyCode);
            return;
        }

        if (e.KeyCode == _supportKey && e.Modifiers == Keys.None && CanUseSupportCar())
        {
            OpenSupportMenu();
            return;
        }

        if (e.KeyCode != _toggleKey || e.Modifiers != Keys.None)
        {
            return;
        }

        if (_teamCarRequested)
        {
            _teamCarRequested = false;
            DismissTeamCar(true);
        }
        else
        {
            _teamCarRequested = true;
            SpawnTeamCar(false);
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        UpdateServiceProp();

        if (!Exists(_car))
        {
            if (_teamCarRequested && Game.GameTime >= _nextRespawnAt)
            {
                DismissTeamCar(false);
                SpawnTeamCar(true);
            }
            return;
        }

        Ped player = Game.Player.Character;
        if (!Exists(player))
        {
            return;
        }

        // Cycletopia only converts ambient vehicles. It explicitly excludes a
        // persistent vehicle or one with a persistent driver. Reassert both
        // states because other traffic scripts can release mission entities.
        ProtectFromTrafficConverters();

        if (_car.IsDead || !Exists(_driver) || _driver.IsDead)
        {
            if (_respawnWhenLost)
            {
                ReplaceTeamCar("Support car replaced");
            }
            else
            {
                _teamCarRequested = false;
                DismissTeamCar(false);
            }
            return;
        }

        float distance = _car.Position.DistanceTo(player.Position);
        UpdateSupportInteraction(player, distance);

        float recoveryDistance = _gtBikeVProtection
            ? Math.Min(_lostDistance, _gtBikeVMaximumDistance)
            : _lostDistance;
        if (_respawnWhenLost && distance > recoveryDistance)
        {
            ReplaceTeamCar("Support car returned");
            return;
        }

        if (_supportMenuOpen && !IsCycling(player))
        {
            return;
        }

        UpdateStuckState(distance);
        if (Game.GameTime >= _nextRetaskAt)
        {
            AssignFollowTask();
        }
    }

    private void UpdateSupportInteraction(Ped player, float distance)
    {
        bool cycling = IsCycling(player);
        float activeRange = cycling ? _cyclingSupportRange : _supportRange;

        if (_supportMenuOpen)
        {
            if (distance > activeRange + 2.0f || (player.IsInVehicle() && !cycling))
            {
                CloseSupportMenu();
                return;
            }

            if (!cycling)
            {
                Function.Call(Hash.SET_VEHICLE_HANDBRAKE, _car.Handle, true);
            }
            GTA.UI.Screen.ShowSubtitle(BuildSupportMenu(), 100);
            return;
        }

        if (distance <= activeRange && (!player.IsInVehicle() || cycling))
        {
            string mode = cycling ? "rolling team car support" : "team car support";
            GTA.UI.Screen.ShowHelpTextThisFrame("Press ~INPUT_CONTEXT~ for " + mode + ".");
        }
    }

    private bool CanUseSupportCar()
    {
        Ped player = Game.Player.Character;
        bool cycling = IsCycling(player);
        return Exists(_car)
            && Exists(_driver)
            && Exists(player)
            && (!player.IsInVehicle() || cycling)
            && player.Position.DistanceTo(_car.Position)
                <= (cycling ? _cyclingSupportRange : _supportRange);
    }

    private void OpenSupportMenu()
    {
        _supportMenuOpen = true;
        _supportMenuIndex = 0;
        if (!IsCycling(Game.Player.Character))
        {
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, _car.Handle, true);
        }
    }

    private void CloseSupportMenu()
    {
        _supportMenuOpen = false;
        if (Exists(_car))
        {
            Function.Call(Hash.SET_VEHICLE_HANDBRAKE, _car.Handle, false);
        }

        _nextRetaskAt = 0;
    }

    private void HandleSupportMenuKey(Keys key)
    {
        if (key == Keys.Up)
        {
            _supportMenuIndex =
                (_supportMenuIndex + SupportItems.Length - 1) % SupportItems.Length;
        }
        else if (key == Keys.Down)
        {
            _supportMenuIndex = (_supportMenuIndex + 1) % SupportItems.Length;
        }
        else if (key == Keys.Enter)
        {
            UseSelectedSupportItem();
        }
        else if (key == Keys.Back || key == Keys.Escape || key == _supportKey)
        {
            CloseSupportMenu();
        }
    }

    private string BuildSupportMenu()
    {
        bool cycling = IsCycling(Game.Player.Character);
        string menu = "~b~TEAM CAR SUPPORT~s~";
        for (int index = 0; index < SupportItems.Length; index++)
        {
            string marker = index == _supportMenuIndex ? "~y~> " : "~s~  ";
            string restriction = cycling && index >= 2 ? " ~c~(stop first)~s~" : "";
            menu += "\n" + marker + SupportItems[index] + restriction + "~s~";
        }
        return menu + "\n~c~Up/Down: choose   Enter: take   Back: close~s~";
    }

    private void UseSelectedSupportItem()
    {
        if (Game.GameTime < _nextSupplyAt)
        {
            int seconds = Math.Max(1, (_nextSupplyAt - Game.GameTime + 999) / 1000);
            Notification.Show("~b~Team Car:~s~ supplies ready in " + seconds + "s.");
            return;
        }

        Ped player = Game.Player.Character;
        if (!Exists(player))
        {
            return;
        }

        if (IsCycling(player) && _supportMenuIndex >= 2)
        {
            Notification.Show("~b~Team Car:~s~ stop and dismount for this service.");
            return;
        }

        bool supplied = true;
        switch (_supportMenuIndex)
        {
            case 0:
                CreateServiceProp(
                    player,
                    "prop_ld_flow_bottle",
                    Bone.SkelLeftHand,
                    new Vector3(0.122f, -0.038f, 0.033f),
                    new Vector3(-103.2f, -68.1f, 2.8f),
                    2500);
                PlayServiceAnimation(player, "mp_player_intdrink", "loop_bottle", 2500, 48);
                Function.Call(Hash.RESTORE_PLAYER_STAMINA, Game.Player.Handle, 1.0f);
                player.Health = Math.Min(player.MaxHealth, player.Health + _waterHealth);
                Notification.Show("~b~Team Car:~s~ water bottle received. Stamina restored.");
                break;
            case 1:
                CreateServiceProp(
                    player,
                    "prop_choc_ego",
                    Bone.PHLeftHand,
                    new Vector3(0.0f, 0.0f, 0.0f),
                    Vector3.Zero,
                    2200);
                PlayServiceAnimation(
                    player,
                    "mp_player_inteat@burger",
                    "mp_player_int_eat_burger",
                    2200,
                    48);
                Function.Call(Hash.RESTORE_PLAYER_STAMINA, Game.Player.Handle, 1.0f);
                Notification.Show("~b~Team Car:~s~ energy gel received. Stamina restored.");
                break;
            case 2:
                CreateServiceProp(
                    player,
                    "prop_ld_health_pack",
                    Bone.PHLeftHand,
                    new Vector3(0.10f, 0.02f, -0.02f),
                    new Vector3(0.0f, 90.0f, 0.0f),
                    3000);
                PlayServiceAnimation(
                    player,
                    "amb@medic@standing@tendtodead@idle_a",
                    "idle_a",
                    3000,
                    1);
                player.Health = Math.Min(player.MaxHealth, player.Health + _firstAidHealth);
                Notification.Show("~b~Team Car:~s~ first aid applied.");
                break;
            case 3:
                supplied = RepairNearestBicycle(player);
                break;
        }

        if (supplied)
        {
            _nextSupplyAt = Game.GameTime + _supplyCooldownMilliseconds;
            CloseSupportMenu();
        }
    }

    private bool RepairNearestBicycle(Ped player)
    {
        Vehicle nearest = null;
        float nearestDistance = _repairRange + 1.0f;
        foreach (Vehicle vehicle in World.GetAllVehicles())
        {
            if (!Exists(vehicle) || vehicle.Handle == _car.Handle)
            {
                continue;
            }

            if (Function.Call<int>(Hash.GET_VEHICLE_CLASS, vehicle.Handle) != 13)
            {
                continue;
            }

            float distance = vehicle.Position.DistanceTo(player.Position);
            if (distance <= _repairRange && distance < nearestDistance)
            {
                nearest = vehicle;
                nearestDistance = distance;
            }
        }

        if (!Exists(nearest))
        {
            Notification.Show("~b~Team Car:~s~ bring your bicycle within "
                + _repairRange.ToString("0.#", CultureInfo.InvariantCulture) + "m.");
            return false;
        }

        Function.Call(Hash.SET_VEHICLE_FIXED, nearest.Handle);
        Function.Call(Hash.SET_VEHICLE_DEFORMATION_FIXED, nearest.Handle);
        Function.Call(Hash.SET_VEHICLE_DIRT_LEVEL, nearest.Handle, 0.0f);
        CreateServiceProp(
            player,
            "prop_tool_wrench",
            Bone.PHRightHand,
            new Vector3(0.11f, 0.02f, 0.0f),
            new Vector3(0.0f, 90.0f, 0.0f),
            3500);
        PlayServiceAnimation(player, "mini@repair", "fixing_a_ped", 3500, 1);
        Notification.Show("~b~Team Car:~s~ bicycle repaired and cleaned.");
        return true;
    }

    private void PlayServiceAnimation(
        Ped player,
        string dictionary,
        string animation,
        int duration,
        int flags)
    {
        if (!_serviceAnimations || !Exists(player))
        {
            return;
        }

        Function.Call(Hash.REQUEST_ANIM_DICT, dictionary);
        int timeout = Game.GameTime + 1000;
        while (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dictionary)
            && Game.GameTime < timeout)
        {
            Wait(10);
        }

        if (!Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, dictionary))
        {
            return;
        }

        Function.Call(
            Hash.TASK_PLAY_ANIM,
            player.Handle,
            dictionary,
            animation,
            4.0f,
            -4.0f,
            duration,
            flags,
            0.0f,
            false,
            false,
            false);
    }

    private void CreateServiceProp(
        Ped player,
        string modelName,
        Bone handBone,
        Vector3 handOffset,
        Vector3 handRotation,
        int duration)
    {
        CleanupServiceProp();
        if (!_serviceAnimations || !Exists(player))
        {
            return;
        }

        Model model = new Model(modelName);
        if (!model.IsInCdImage || !model.IsValid)
        {
            return;
        }

        model.Request(500);
        if (!model.IsLoaded)
        {
            model.MarkAsNoLongerNeeded();
            return;
        }

        _serviceProp = World.CreateProp(model, player.Position, false, false);
        model.MarkAsNoLongerNeeded();
        if (!Exists(_serviceProp))
        {
            _serviceProp = null;
            return;
        }

        _serviceProp.IsPersistent = true;
        _serviceProp.AttachTo(player.Bones[handBone], handOffset, handRotation);
        _servicePropDeleteAt = Game.GameTime + duration;
    }

    private void UpdateServiceProp()
    {
        if (Exists(_serviceProp) && Game.GameTime >= _servicePropDeleteAt)
        {
            CleanupServiceProp();
        }
    }

    private void CleanupServiceProp()
    {
        if (Exists(_serviceProp))
        {
            _serviceProp.Delete();
        }

        _serviceProp = null;
        _servicePropDeleteAt = 0;
    }

    private static bool IsCycling(Ped player)
    {
        return Exists(player)
            && player.IsInVehicle()
            && Exists(player.CurrentVehicle)
            && Function.Call<int>(Hash.GET_VEHICLE_CLASS, player.CurrentVehicle.Handle) == 13;
    }

    private void ProtectFromTrafficConverters()
    {
        if (Exists(_car))
        {
            _car.IsPersistent = true;
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _car.Handle, true, true);
            if (_gtBikeVProtection)
            {
                _car.IsInvincible = true;
            }
            ApplyBicycleRoofVariant();
        }

        if (Exists(_driver))
        {
            _driver.IsPersistent = true;
            _driver.BlockPermanentEvents = true;
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _driver.Handle, true, true);
            if (_gtBikeVProtection)
            {
                _driver.IsInvincible = true;
            }
        }
    }

    private void SpawnTeamCar(bool silent)
    {
        _nextRespawnAt = Game.GameTime + 3000;
        Ped player = Game.Player.Character;
        if (!Exists(player))
        {
            return;
        }

        Model carModel = new Model(_vehicleModel);
        Model driverModel = new Model(_driverModel);

        if (!carModel.IsInCdImage || !carModel.IsVehicle)
        {
            Notification.Show("~r~Team Car:~s~ vehicle model '" + _vehicleModel + "' is unavailable.");
            return;
        }

        if (!driverModel.IsInCdImage || !driverModel.IsPed)
        {
            Notification.Show("~r~Team Car:~s~ driver model '" + _driverModel + "' is unavailable.");
            return;
        }

        carModel.Request(1000);
        driverModel.Request(1000);
        if (!carModel.IsLoaded || !driverModel.IsLoaded)
        {
            Notification.Show("~r~Team Car:~s~ models did not load.");
            carModel.MarkAsNoLongerNeeded();
            driverModel.MarkAsNoLongerNeeded();
            return;
        }

        Vector3 spawn = FindRoadSpawn(player);
        _car = World.CreateVehicle(carModel, spawn, player.Heading);
        if (!Exists(_car))
        {
            Notification.Show("~r~Team Car:~s~ could not create the vehicle.");
            carModel.MarkAsNoLongerNeeded();
            driverModel.MarkAsNoLongerNeeded();
            return;
        }

        _car.IsPersistent = true;
        _car.IsInvincible = _gtBikeVProtection;
        _car.PlaceOnGround();
        _car.IsEngineRunning = true;
        _car.CanTiresBurst = false;
        Function.Call(Hash.SET_VEHICLE_COLOURS, _car.Handle, _primaryColour, _secondaryColour);
        Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, _car.Handle, TrimPlate(_plate));
        ApplyBicycleRoofVariant();

        _driver = World.CreatePed(driverModel, _car.Position);
        if (!Exists(_driver))
        {
            _car.Delete();
            _car = null;
            Notification.Show("~r~Team Car:~s~ could not create the driver.");
            carModel.MarkAsNoLongerNeeded();
            driverModel.MarkAsNoLongerNeeded();
            return;
        }

        _driver.IsPersistent = true;
        _driver.IsInvincible = _gtBikeVProtection;
        _driver.BlockPermanentEvents = true;
        _driver.SetIntoVehicle(_car, VehicleSeat.Driver);
        Function.Call(Hash.SET_DRIVER_ABILITY, _driver.Handle, 1.0f);
        Function.Call(Hash.SET_DRIVER_AGGRESSIVENESS, _driver.Handle, 0.0f);
        Function.Call(Hash.SET_PED_KEEP_TASK, _driver.Handle, true);

        _blip = _car.AddBlip();
        if (Exists(_blip))
        {
            _blip.Name = "Team Car";
            _blip.Color = BlipColor.Blue;
            _blip.IsShortRange = true;
        }

        carModel.MarkAsNoLongerNeeded();
        driverModel.MarkAsNoLongerNeeded();
        _recovering = false;
        _stuckSince = 0;
        AssignFollowTask();

        if (!silent)
        {
            Notification.Show("~b~Team Car~s~ dispatched. Press ~y~" + _toggleKey + "~s~ to dismiss.");
        }
    }

    private Vector3 FindRoadSpawn(Ped player)
    {
        Vector3 desired = player.Position - player.ForwardVector * Math.Max(18.0f, _followDistance + 6.0f);
        Vector3 roadside = World.GetNextPositionOnStreet(desired, true);
        if (roadside == Vector3.Zero || roadside.DistanceTo(player.Position) > 90.0f)
        {
            roadside = World.GetNextPositionOnStreet(player.Position, true);
        }
        return roadside;
    }

    private void ApplyBicycleRoofVariant()
    {
        if (!Exists(_car))
        {
            return;
        }

        if (Function.Call<bool>(Hash.DOES_EXTRA_EXIST, _car.Handle, _surfboardRoofExtra))
        {
            // The final native argument means "disable", so true removes it.
            Function.Call(Hash.SET_VEHICLE_EXTRA, _car.Handle, _surfboardRoofExtra, true);
        }

        if (Function.Call<bool>(Hash.DOES_EXTRA_EXIST, _car.Handle, _bicycleRoofExtra))
        {
            Function.Call(Hash.SET_VEHICLE_EXTRA, _car.Handle, _bicycleRoofExtra, false);
        }
    }

    private void AssignFollowTask()
    {
        if (!Exists(_driver) || !Exists(_car))
        {
            return;
        }

        Ped player = Game.Player.Character;
        if (!Exists(player))
        {
            return;
        }

        float playerSpeed = player.Velocity.Length();
        float desiredSpeed = Math.Min(_maximumSpeed, Math.Max(_cruiseSpeed, playerSpeed + _speedBuffer));

        Function.Call(
            Hash.TASK_VEHICLE_FOLLOW,
            _driver.Handle,
            _car.Handle,
            player.Handle,
            desiredSpeed,
            _drivingStyle,
            (int)Math.Round(_followDistance));

        Function.Call(Hash.SET_PED_KEEP_TASK, _driver.Handle, true);
        _nextRetaskAt = Game.GameTime + _retaskInterval;
    }

    private void UpdateStuckState(float distance)
    {
        if (distance < _followDistance * 1.5f || _car.Speed > 1.0f)
        {
            _stuckSince = 0;
            _recovering = false;
            return;
        }

        if (_stuckSince == 0)
        {
            _stuckSince = Game.GameTime;
            return;
        }

        if (!_recovering && Game.GameTime - _stuckSince >= _stuckMilliseconds)
        {
            _recovering = true;
            Function.Call(Hash.SET_VEHICLE_FORWARD_SPEED, _car.Handle, 2.0f);
            AssignFollowTask();
            _stuckSince = Game.GameTime;
        }
    }

    private void ReplaceTeamCar(string message)
    {
        DismissTeamCar(false);
        SpawnTeamCar(true);
        if (Exists(_car))
        {
            Notification.Show("~b~Team Car:~s~ " + message + ".");
        }
    }

    private void DismissTeamCar(bool notify)
    {
        _supportMenuOpen = false;
        if (Exists(_blip))
        {
            _blip.Delete();
        }

        if (Exists(_driver))
        {
            _driver.Task.ClearAllImmediately();
            _driver.Delete();
        }

        if (Exists(_car))
        {
            _car.Delete();
        }

        _blip = null;
        _driver = null;
        _car = null;
        _stuckSince = 0;
        _recovering = false;

        if (notify)
        {
            Notification.Show("~b~Team Car~s~ dismissed.");
        }
    }

    private void OnAborted(object sender, EventArgs e)
    {
        _teamCarRequested = false;
        CleanupServiceProp();
        DismissTeamCar(false);
    }

    private static string TrimPlate(string text)
    {
        string value = string.IsNullOrWhiteSpace(text) ? "TEAM CAR" : text.Trim();
        return value.Length <= 8 ? value : value.Substring(0, 8);
    }

    private static bool Exists(Entity entity)
    {
        return entity != null && entity.Exists();
    }

    private static bool Exists(Blip blip)
    {
        return blip != null && blip.Exists();
    }
}
