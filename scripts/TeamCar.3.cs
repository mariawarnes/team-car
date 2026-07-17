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
    private Keys _toggleKey = Keys.F10;
    private string _vehicleModel = "stratum";
    private string _driverModel = "s_m_m_autoshop_01";
    private string _plate = "TEAM CAR";
    private int _primaryColour = 3;
    private int _secondaryColour = 111;
    private float _followDistance = 12.0f;
    private float _cruiseSpeed = 18.0f;
    private float _maximumSpeed = 32.0f;
    private float _speedBuffer = 5.0f;
    private int _drivingStyle = 786603;
    private int _retaskInterval = 5000;
    private bool _respawnWhenLost = true;
    private float _lostDistance = 180.0f;
    private int _stuckMilliseconds = 8000;

    private int _nextRetaskAt;
    private int _stuckSince;
    private bool _recovering;

    public TeamCar()
    {
        Interval = 100;
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
            _vehicleModel = settings.GetValue("Vehicle", "Model", "stratum");
            _driverModel = settings.GetValue("Vehicle", "DriverModel", "s_m_m_autoshop_01");
            _primaryColour = settings.GetValue("Vehicle", "PrimaryColour", 3);
            _secondaryColour = settings.GetValue("Vehicle", "SecondaryColour", 111);
            _plate = settings.GetValue("Vehicle", "Plate", "TEAM CAR");
            _followDistance = Math.Max(6.0f, settings.GetValue("Following", "FollowDistance", 12.0f));
            _cruiseSpeed = Math.Max(4.0f, settings.GetValue("Following", "CruiseSpeed", 18.0f));
            _maximumSpeed = Math.Max(_cruiseSpeed, settings.GetValue("Following", "MaximumSpeed", 32.0f));
            _speedBuffer = Math.Max(1.0f, settings.GetValue("Following", "SpeedBuffer", 5.0f));
            _drivingStyle = settings.GetValue("Following", "DrivingStyle", 786603);
            _retaskInterval = Math.Max(2000, settings.GetValue("Following", "RetaskIntervalMs", 5000));
            _respawnWhenLost = settings.GetValue("Recovery", "RespawnWhenLost", true);
            _lostDistance = Math.Max(60.0f, settings.GetValue("Recovery", "LostDistance", 180.0f));
            _stuckMilliseconds = Math.Max(3000, settings.GetValue("Recovery", "StuckSeconds", 8)) * 1000;
        }
        catch (Exception exception)
        {
            Notification.Show("~r~Team Car config error:~s~ " + exception.Message);
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != _toggleKey || e.Modifiers != Keys.None)
        {
            return;
        }

        if (Exists(_car))
        {
            DismissTeamCar(true);
        }
        else
        {
            SpawnTeamCar(false);
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        if (!Exists(_car))
        {
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
                DismissTeamCar(false);
            }
            return;
        }

        float distance = _car.Position.DistanceTo(player.Position);
        if (_respawnWhenLost && distance > _lostDistance)
        {
            ReplaceTeamCar("Support car returned");
            return;
        }

        UpdateStuckState(distance);

        if (Game.GameTime >= _nextRetaskAt)
        {
            AssignFollowTask();
        }
    }

    private void ProtectFromTrafficConverters()
    {
        if (Exists(_car))
        {
            _car.IsPersistent = true;
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _car.Handle, true, true);
        }

        if (Exists(_driver))
        {
            _driver.IsPersistent = true;
            _driver.BlockPermanentEvents = true;
            Function.Call(Hash.SET_ENTITY_AS_MISSION_ENTITY, _driver.Handle, true, true);
        }
    }

    private void SpawnTeamCar(bool silent)
    {
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
        _car.PlaceOnGround();
        _car.IsEngineRunning = true;
        _car.CanTiresBurst = false;
        Function.Call(Hash.SET_VEHICLE_COLOURS, _car.Handle, _primaryColour, _secondaryColour);
        Function.Call(Hash.SET_VEHICLE_NUMBER_PLATE_TEXT, _car.Handle, TrimPlate(_plate));

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
