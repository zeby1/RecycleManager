using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Oxide.Core.Configuration;
using UnityEngine;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("RecycleManager", "redBDGR", "1.0.15")]
    [Description("Easily change features about the recycler")]

    class RecycleManager : RustPlugin
    {
        private bool changed;

        #region Data

        #endregion

        public float recycleTime = 5.0f;
        private const string permissionNameADMIN = "recyclemanager.admin";
        private const string permissionNameCREATE = "recyclemanager.create";
        private int maxItemsPerRecycle = 100;

        private static Dictionary<string, object> Multipliers()
        {
            var at = new Dictionary<string, object> { { "*", 1 }, { "metal.refined", 1 } };
            return at;
        }

        private static List<object> Blacklist()
        {
            var at = new List<object> { "hemp.seed" };
            return at;
        }

        private static List<object> OutputBlacklist()
        {
            var at = new List<object> { "hemp.seed" };
            return at;
        }

        private List<object> blacklistedItems;
        private List<object> outputBlacklistedItems;
        private Dictionary<string, object> multiplyList;

        private void LoadVariables()
        {
            blacklistedItems = (List<object>)GetConfig("Lists", "Input Blacklist", Blacklist());
            recycleTime = Convert.ToSingle(GetConfig("Settings", "Recycle Time", 5.0f));
            multiplyList = (Dictionary<string, object>)GetConfig("Lists", "Recycle Output Multipliers", Multipliers());
            maxItemsPerRecycle = Convert.ToInt32(GetConfig("Settings", "Max Items Per Recycle", 100));
            outputBlacklistedItems = (List<object>)GetConfig("Lists", "Output Blacklist", OutputBlacklist());

            if (!changed) return;
            SaveConfig();
            changed = false;
        }

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            LoadVariables();
        }

        private void Init()
        {
            LoadVariables();
            permission.RegisterPermission(permissionNameADMIN, this);
            permission.RegisterPermission(permissionNameCREATE, this);

            lang.RegisterMessages(new Dictionary<string, string>
            {
                //chat
                ["No Permissions"] = "You cannot use this command!",
                ["addrecycler CONSOLE invalid syntax"] = "Invalid syntax! addrecycler <playername/id>",
                ["No Player Found"] = "No player was found or they are offline",
                ["AddRecycler CONSOLE success"] = "A recycler was successfully placed at the players location!",
                ["AddRecycler CannotPlace"] = "You cannot place a recycler here",
                ["RemoveRecycler CHAT NoEntityFound"] = "There were no valid entities found",
                ["RemoveRecycler CHAT EntityWasRemoved"] = "The targeted entity was removed",

            }, this);
        }

        [ChatCommand("addrecycler")]
        private void AddRecyclerCMD(BasePlayer player, string command, String[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permissionNameCREATE) && !permission.UserHasPermission(player.UserIDString, permissionNameADMIN))
            {
                player.ChatMessage(msg("No Permissions", player.UserIDString));
                return;
            }

            if (!player.IsBuildingAuthed())
            {
                if (!permission.UserHasPermission(player.UserIDString, permissionNameADMIN))
                {
                    player.ChatMessage(msg("AddRecycler CannotPlace", player.UserIDString));
                    return;
                }
            }

            BaseEntity ent = GameManager.server.CreateEntity("assets/bundled/prefabs/static/recycler_static.prefab", player.transform.position, player.GetNetworkRotation(), true);
            ent.Spawn();
            return;
        }

        [ConsoleCommand("recyclemanager.addrecycler")]
        private void AddRecyclerCMDConsole(ConsoleSystem.Arg arg)
        {
            if (arg?.Args == null)
            {
                arg.ReplyWith(msg("addrecycler CONSOLE invalid syntax"));
                return;
            }
            if (arg.Connection != null) return;
            if (arg.Args.Length != 1)
            {
                arg.ReplyWith(msg("addrecycler CONSOLE invalid syntax"));
                return;
            }
            BasePlayer target = FindPlayer(arg.Args[0]);
            if (target == null || !target.IsValid())
            {
                arg.ReplyWith(msg("No Player Found"));
                return;
            }
            BaseEntity ent = GameManager.server.CreateEntity("assets/bundled/prefabs/static/recycler_static.prefab", target.transform.position, target.GetNetworkRotation(), true);
            ent.Spawn();
            arg.ReplyWith(msg("AddRecycler CONSOLE success"));
        }

        [ChatCommand("removerecycler")]
        private void RemoveRecyclerCMD(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, permissionNameADMIN))
            {
                player.ChatMessage(msg("No Permissions", player.UserIDString));
                return;
            }
            RaycastHit hit;
            Physics.Raycast(player.eyes.HeadRay(), out hit);
            if (hit.GetEntity() == null)
            {
                player.ChatMessage(msg("RemoveRecycler CHAT NoEntityFound", player.UserIDString));
                return;
            }
            BaseEntity ent = hit.GetEntity();
            if (!ent.name.Contains("recycler"))
            {
                player.ChatMessage(msg("RemoveRecycler CHAT NoEntityFound", player.UserIDString));
                return;
            }
            ent.Kill();
            player.ChatMessage(msg("RemoveRecycler CHAT EntityWasRemoved", player.UserIDString));
        }

        private void OnRecyclerToggle(Recycler recycler, BasePlayer player)
        {
            if (recycler.IsOn())
            {
                recycler.CancelInvoke("RecycleThink");
                return;
            }
            recycler.CancelInvoke("RecycleThink");
            timer.Once(0.1f, () => { recycler.Invoke("RecycleThink", recycleTime); });
        }

        private object CanRecycle(Recycler recycler, Item item)
        {
            if (blacklistedItems.Contains(item.info.shortname) || item.info.Blueprint == null || item.info.Blueprint.ingredients?.Count == 0)
                return false;
            return true;
        }

        private object OnRecycleItem(Recycler recycler, Item item)
        {

            var bp = ItemManager.FindItemDefinition(item.info.itemid).Blueprint;
            if (bp == null || bp.ingredients?.Count == 0)
                return false;

            int usedItems = 1;

            if (item.amount > 1)
                usedItems = item.amount;
            if (usedItems > maxItemsPerRecycle)
                usedItems = maxItemsPerRecycle;
            item.UseItem(usedItems);

            foreach (ItemAmount ingredient in bp.ingredients)
            {
                var shortname = ingredient.itemDef.shortname;
                double multi = 1;
                if (multiplyList.ContainsKey("*"))
                    multi = Convert.ToDouble(multiplyList["*"]);
                if (multiplyList.ContainsKey(shortname))
                    multi = Convert.ToDouble(multiplyList[shortname]);
                double outputamount = 0;
                if (shortname == "scrap")
                    outputamount = Convert.ToDouble(usedItems * Convert.ToDouble(bp.scrapFromRecycle) * multi);
                else
                    outputamount = Convert.ToDouble(usedItems * Convert.ToDouble(ingredient.amount / (2*bp.amountToCreate)) * multi);

                // when the batch is returning less than 1 we'll give the chance to return it.
                // For large batches users will get back the percentage ie 25% pipes out of 10 HE grens ends up being 2 pipes every time, but 1 HE will give 25% chance
                if (outputamount < 1)
                {
                    var rnd = Oxide.Core.Random.Range(100);
                    if (rnd < outputamount * 100)
                        outputamount = 1;
                    else
                        continue;
                }

                if (!recycler.MoveItemToOutput(ItemManager.CreateByItemID(ingredient.itemDef.itemid, Convert.ToInt32(outputamount))) || !recycler.HasRecyclable())
                {
                    recycler.StopRecycling();
                    return false;
                }
                   
            }
            return true;
        }

        private object GetConfig(string menu, string datavalue, object defaultValue)
        {
            var data = Config[menu] as Dictionary<string, object>;
            if (data == null)
            {
                data = new Dictionary<string, object>();
                Config[menu] = data;
                changed = true;
            }
            object value;
            if (data.TryGetValue(datavalue, out value)) return value;
            value = defaultValue;
            data[datavalue] = value;
            changed = true;
            return value;
        }

        private static BasePlayer FindPlayer(string nameOrId)
        {
            foreach (var activePlayer in BasePlayer.activePlayerList)
            {
                if (activePlayer.UserIDString == nameOrId)
                    return activePlayer;
                if (activePlayer.displayName.Contains(nameOrId, CompareOptions.OrdinalIgnoreCase))
                    return activePlayer;
                if (activePlayer.net?.connection != null && activePlayer.net.connection.ipaddress == nameOrId)
                    return activePlayer;
            }
            return null;
        }

        private string msg(string key, string id = null) => lang.GetMessage(key, this, id);
    }
}
