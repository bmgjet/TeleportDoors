using Network;
using Oxide.Core;
using Oxide.Core.Plugins;
using ProtoBuf;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("TeleportDoors", "bmgjet", "1.0.4")]
    [Description("Using this door/button will take you to its teleport location")]
    public class TeleportDoors : RustPlugin
    {
        [PluginReference]
        private Plugin Vanish;
        public const string permUse = "TeleportDoors.use";
        private class TPEntity { public Vector3 TPLocation; public string Name = "Going To New Zone"; public string CMD = ""; public string SFX = ""; }
        Dictionary<BaseEntity, TPEntity> _TPEntity = new Dictionary<BaseEntity, TPEntity>();
        private void Init() { permission.RegisterPermission(permUse, this); }
        private void OnServerInitialized(bool initial) { if (initial) { Fstartup(); return; } Startup(); }
        object OnPlayerRespawn(BasePlayer player) { player.ClientRPCPlayer(null, player, "StartLoading_Quick"); AdjustConnectionScreen(player, "Loading"); player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true); player.SendEntityUpdate(); return null; }
        private bool IsInvisible(BasePlayer player) { return Vanish != null && Vanish.Call<bool>("IsInvisible", player); }
        private void AdjustConnectionScreen(BasePlayer player, string msg) 
        {
            if (!Network.Net.sv.IsConnected())
            {
                return;
            }
            NetWrite netWrite = Network.Net.sv.StartWrite();
            netWrite.PacketID(Message.Type.Message); 
            netWrite.String(msg);
            netWrite.Send(new SendInfo(player.Connection)); 
        }

        private void OnDoorOpened(Door thisdoor, BasePlayer player) { if (thisdoor == null || player == null || thisdoor.OwnerID != 0) { return; } if (_TPEntity.ContainsKey(thisdoor) && permission.UserHasPermission(player.UserIDString, permUse)) { Teleport(player, _TPEntity[thisdoor]); thisdoor.Invoke(thisdoor.CloseRequest, 0.5f); return; } if (_TPEntity.ContainsKey(thisdoor)) thisdoor.CloseRequest(); }
        private void OnButtonPress(PressButton thisbutton, BasePlayer player) { if (thisbutton == null || player == null || thisbutton.OwnerID != 0) { return; } if (_TPEntity.ContainsKey(thisbutton) && permission.UserHasPermission(player.UserIDString, permUse)) { Teleport(player, _TPEntity[thisbutton]); thisbutton.pressDuration = 0.1f; } }
        private void Fstartup() { timer.Once(10f, () => { try { if (Rust.Application.isLoading) { Fstartup(); return; } } catch { } Startup(); }); }
        private void Startup()
        {
            _TPEntity.Clear();
            foreach (PrefabData prefabdata in World.Serialization.world.prefabs)
            {
                if (!prefabdata.category.ToUpper().Contains("TELEPORT=")) { continue; }
                string settings = prefabdata.category.Split(':')[1].Replace("\\", "");
                BaseEntity _foundTrigger = FindDoor(prefabdata.position, 1.4f);
                if (_foundTrigger == null || _TPEntity.ContainsKey(_foundTrigger) || settings == null) { return; }
                string[] ParsedSettings = settings.Split('=');
                if (ParsedSettings.Count() > 0)
                {
                    TPEntity DoorSettings = new TPEntity();
                    string[] Vec = ParsedSettings[1].Split(',');
                    try { DoorSettings.Name = ParsedSettings[2]; } catch { }
                    try { DoorSettings.SFX = StringPool.toString[uint.Parse(ParsedSettings[3])]; } catch { }
                    try { DoorSettings.CMD = ParsedSettings[4]; } catch { }
                    if (Vec.Count() == 3) { DoorSettings.TPLocation = new Vector3(float.Parse(Vec[0]), float.Parse(Vec[1]), float.Parse(Vec[2])); _TPEntity.Add(_foundTrigger, DoorSettings); continue; }
                    Puts("Error parsing teleporter @ " + _foundTrigger.transform.position.ToString());
                }
            }
            Puts("Found " + _TPEntity.Count.ToString() + " Teleporters");
        }

        private void Teleport(BasePlayer player, TPEntity tpdoor)
        {
            if (!player.IsValid() || Vector3.Distance(tpdoor.TPLocation, default(Vector3)) < 5f) return;
            if (tpdoor.SFX != "") { Effect.server.Run(tpdoor.SFX, player.transform.position); }
            player.Invoke(() =>
            {
                try
                {
                    player.EnsureDismounted();
                    if (player.HasParent())
                    {
                        player.SetParent(null, true, true);
                    }

                    if (player.IsConnected)
                    {
                        player.EndLooting();
                        StartSleeping(player);
                    }

                    player.Teleport(tpdoor.TPLocation);

                    if (player.IsConnected && !Network.Net.sv.visibility.IsInside(player.net.group, tpdoor.TPLocation))
                    {
                        player.SetPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot, true);
                        player.ClientRPCPlayer(null, player, "StartLoading_Quick");
                        player.SendEntityUpdate();
                        player.UpdateNetworkGroup();
                        player.SendNetworkUpdateImmediate(false);
                    }
                }
                finally
                {
                    timer.Once(1f, () =>
                    {
                        player.ForceUpdateTriggers(true, true, true);
                        player.ForceUpdateTriggers();
                        Wakeup(player, tpdoor);
                    });
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
            timer.Once(1f, () => { player.EndSleeping(); });
        }

        BaseEntity FindDoor(Vector3 pos, float radius)
        {
            foreach (BaseNetworkable baseNetworkable in BaseNetworkable.serverEntities.entityList.Values)
            {
                if ((baseNetworkable is Door || baseNetworkable is PressButton) && baseNetworkable.transform.position == pos)
                {
                    return baseNetworkable as BaseEntity;
                }
            }
            return null;
        }
    }
}
