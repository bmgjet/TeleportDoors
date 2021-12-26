using Network;
using Oxide.Core;
using Oxide.Core.Plugins;
using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace Oxide.Plugins
{
    [Info("TeleportDoors", "bmgjet", "1.0.0")]
    [Description("Using this door will take you to its teleport location")]
    public class TeleportDoors : RustPlugin
    {
        private class TPDoor { public Vector3 TPLocation; public string Name = "Going To New Zone"; }
        [PluginReference]
        private Plugin Vanish;
        Dictionary<Door, TPDoor> _TeleportDoors = new Dictionary<Door, TPDoor>();
        private void OnServerInitialized(bool initial) { if (initial) { Fstartup(); return; } Startup(); }
        private bool IsInvisible(BasePlayer player) { return Vanish != null && Vanish.Call<bool>("IsInvisible", player); }
        private void Fstartup() { timer.Once(10f, () => { try { if (Rust.Application.isLoading) { Fstartup(); return; } } catch { } Startup(); }); }
        private void Startup()
        {
            _TeleportDoors.Clear();
            for (int i = World.Serialization.world.prefabs.Count - 1; i >= 0; i--)
            {
                PrefabData prefabdata = World.Serialization.world.prefabs[i];
                if (prefabdata.category.Contains("TELEPORT="))
                {
                    string settings = prefabdata.category.Split(':')[1].Replace("\\", "");
                    Door _foundDoor = FindDoor(prefabdata.position, 1.2f);
                    if (_foundDoor == null) continue;
                    if (!_TeleportDoors.ContainsKey(_foundDoor))
                    {
                        if (settings != null)
                        {
                            string[] ParsedSettings = settings.Split('=');
                            if (ParsedSettings.Count() > 1)
                            {
                                TPDoor DoorSettings = new TPDoor();
                                try
                                {
                                    string[] Vec = ParsedSettings[1].Split(',');
                                    try { DoorSettings.Name = ParsedSettings[2]; } catch { }
                                    if (Vec.Count() == 3)
                                    {
                                        DoorSettings.TPLocation = new Vector3(float.Parse(Vec[0]), float.Parse(Vec[1]), float.Parse(Vec[2]));
                                        _TeleportDoors.Add(_foundDoor, DoorSettings);
                                        continue;
                                    }
                                }
                                catch { }
                                Puts("Error parsing custom door @ " + _foundDoor.transform.position.ToString());
                            }
                        }
                    }
                }
            }
            Puts("Found " + _TeleportDoors.Count.ToString() + " Teleport doors");
        }
        private void OnDoorOpened(Door thisdoor, BasePlayer player)
        {
            if (thisdoor == null || player == null) return;
            if (_TeleportDoors.ContainsKey(thisdoor)) { Teleport(player, _TeleportDoors[thisdoor]); thisdoor.CloseRequest(); }
        }

        private void Teleport(BasePlayer player, TPDoor tpdoor)
        {
            if (!player.IsValid()) return;
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
                NextTick(() => { Wakeup(player); });
            }
        }

        private void StartSleeping(BasePlayer player)
        {
            if (!player.IsSleeping())
            {
                Interface.CallHook("OnPlayerSleep", player);
                player.SetPlayerFlag(BasePlayer.PlayerFlags.Sleeping, true);
                player.sleepStartTime = Time.time;
                BasePlayer.sleepingPlayerList.Add(player);
                BasePlayer.bots.Remove(player);
                player.CancelInvoke("InventoryUpdate");
                player.CancelInvoke("TeamUpdate");
                player.SendNetworkUpdateImmediate();
            }
        }

        private void Wakeup(BasePlayer player)
        {
            if (player.IsConnected == false) return;
            if (player.IsReceivingSnapshot == true) { timer.Once(1f, () => Wakeup(player)); return; }
            player.EndSleeping();
        }
        Door FindDoor(Vector3 pos, float radius)
        {
            List<Door> ScanArea = new List<Door>();
            Vis.Entities(pos, radius, ScanArea);
            if (ScanArea.Count == 0){Vis.Entities(pos + new Vector3(0, 3, 0), radius, ScanArea);}
            if (ScanArea.Count != 0){return ScanArea[0];}
            return null;
        }
    }
}