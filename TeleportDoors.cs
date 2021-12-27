using Network;
using Oxide.Core;
using Oxide.Core.Plugins;
using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("TeleportDoors", "bmgjet", "1.0.1")]
    [Description("Using this door/button will take you to its teleport location")]
    public class TeleportDoors : RustPlugin
    {
        [PluginReference]
        private Plugin Vanish;
        public const string permUse = "TeleportDoors.use"; 
        private class TPEntity { public Vector3 TPLocation; public string Name = "Going To New Zone"; public string CMD = ""; public string SFX = ""; }
        Dictionary<BaseEntity, TPEntity> _TPEntity = new Dictionary<BaseEntity, TPEntity>();
        private void Init(){permission.RegisterPermission(permUse, this);}
        private void OnServerInitialized(bool initial) { if (initial) { Fstartup(); return; } Startup(); }
        private bool IsInvisible(BasePlayer player) { return Vanish != null && Vanish.Call<bool>("IsInvisible", player); }
        private void Fstartup() { timer.Once(10f, () => { try { if (Rust.Application.isLoading) { Fstartup(); return; } } catch { } Startup(); }); }
        private void Startup()
        {
            _TPEntity.Clear();
            foreach (PrefabData prefabdata in World.Serialization.world.prefabs)
            {
                if (prefabdata.category.Contains("TELEPORT="))
                {
                    string settings = prefabdata.category.Split(':')[1].Replace("\\", "");
                    BaseEntity _foundTrigger = FindDoor(prefabdata.position, 1.2f);
                    if (_foundTrigger != null && !_TPEntity.ContainsKey(_foundTrigger) && settings != null)
                    {
                        string[] ParsedSettings = settings.Split('=');
                        if (ParsedSettings.Count() > 1)
                        {
                            TPEntity DoorSettings = new TPEntity();
                            string[] Vec = ParsedSettings[1].Split(',');
                            try { DoorSettings.Name = ParsedSettings[2]; } catch { }
                            try { DoorSettings.SFX = StringPool.toString[uint.Parse(ParsedSettings[3])]; } catch { }
                            try { DoorSettings.CMD = ParsedSettings[4]; } catch { }
                            if (Vec.Count() == 3)
                            {
                                DoorSettings.TPLocation = new Vector3(float.Parse(Vec[0]), float.Parse(Vec[1]), float.Parse(Vec[2]));
                                _TPEntity.Add(_foundTrigger, DoorSettings);
                                continue;
                            }
                            Puts("Error parsing teleporter @ " + _foundTrigger.transform.position.ToString());
                        }
                    }
                }
            }
            Puts("Found " + _TPEntity.Count.ToString() + " Teleporters");
        }
        private void OnDoorOpened(Door thisdoor, BasePlayer player)
        {
            if (thisdoor == null || player == null || thisdoor.OwnerID != 0 || !permission.UserHasPermission(player.UserIDString, permUse)){ thisdoor.CloseRequest(); return; }
            if (_TPEntity.ContainsKey(thisdoor)) { Teleport(player, _TPEntity[thisdoor]); thisdoor.Invoke(thisdoor.CloseRequest, 0.5f); }
        }

        private void OnButtonPress(PressButton thisbutton, BasePlayer player)
        {
            if (thisbutton == null || player == null || thisbutton.OwnerID != 0 || !permission.UserHasPermission(player.UserIDString, permUse)) return;
            if (_TPEntity.ContainsKey(thisbutton)) { Teleport(player, _TPEntity[thisbutton]); thisbutton.pressDuration = 0.1f; }
        }

        private void Teleport(BasePlayer player, TPEntity tpdoor)
        {
            if (tpdoor.SFX != "") { Effect.server.Run(tpdoor.SFX, player.transform.position); }
            player.Invoke(() =>
            {
                try
                {
                    player.EnsureDismounted();
                    if (player.HasParent()) { player.SetParent(null, true, true); }
                    if (player.IsConnected) { player.EndLooting(); StartSleeping(player); }
                    player.RemoveFromTriggers();
                    player.SetServerFall(true);
                    player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                    player.ClientRPCPlayer(null, player, "StartLoading");
                    if (Net.sv.write.Start())
                    {
                        Net.sv.write.PacketID(Message.Type.Message);
                        Net.sv.write.String(tpdoor.Name);
                        Net.sv.write.String("");
                        Net.sv.write.Send(new SendInfo(player.Connection));
                    }
                    player.Teleport(tpdoor.TPLocation);
                    player.SendEntityUpdate();
                    if (!IsInvisible(player)) { player.UpdateNetworkGroup(); player.SendNetworkUpdateImmediate(false); }
                }
                finally
                {
                    player.SetServerFall(false);
                    player.ForceUpdateTriggers();
                    NextTick(() => { Wakeup(player, tpdoor); });
                }
            }, 0.5f);
        }

        private void StartSleeping(BasePlayer player)
        {
            if (!player.IsSleeping())
            {
                Interface.CallHook("OnPlayerSleep", player);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
                player.sleepStartTime = Time.time;
                BasePlayer.sleepingPlayerList.Add(player);
                player.CancelInvoke("InventoryUpdate");
                player.CancelInvoke("TeamUpdate");
                player.SendNetworkUpdateImmediate();
            }
        }

        private void Wakeup(BasePlayer player, TPEntity tpdoor)
        {
            if (player.IsConnected == false) return;
            if (player.IsReceivingSnapshot == true) { timer.Once(1f, () => Wakeup(player, tpdoor)); return; }
            if (tpdoor.CMD != "") { string CMD = tpdoor.CMD.Replace("$player", player.displayName).Replace("$steamid", player.OwnerID.ToString()); covalence.Server.Command(CMD); }
            player.EndSleeping();
        }
        BaseEntity FindDoor(Vector3 pos, float radius)
        {
            List<BaseEntity> ScanArea = new List<BaseEntity>();
            Vis.Entities(pos, radius, ScanArea);
            foreach(BaseEntity be in ScanArea)
            {
                if (be is Door || be is PressButton)
                    return be;
            }
            Vis.Entities(pos + new Vector3(0, 3, 0), radius, ScanArea);
            if (ScanArea.Count != 0 && ScanArea[0] is Door) { return ScanArea[0]; }
            return null;
        }
    }
}