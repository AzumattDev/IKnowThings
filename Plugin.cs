using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace IKnowThings
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class IKnowThingsPlugin : BaseUnityPlugin
    {
        internal const string ModName = "IKnowThings";
        internal const string ModVersion = "1.0.0";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;

        private readonly Harmony _harmony = new(ModGUID);

        public static readonly ManualLogSource IKnowThingsLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

        public enum Toggle
        {
            On = 1,
            Off = 0
        }

        public void Awake()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
        }


        #region ConfigOptions

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order = null!;
            [UsedImplicitly] public bool? Browsable = null!;
            [UsedImplicitly] public string? Category = null!;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
        }

        class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() =>
                "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }

        #endregion
    }

    [HarmonyPatch(typeof(Terminal), nameof(Terminal.InitTerminal))]
    static class TerminalInitTerminalPatch
    {
        static void Postfix(Terminal __instance)
        {
            Command("learnrecipes", "Learns all recipes for your player. Only admins can execute this command", (args) =>
            {
                if (Admin.Enabled)
                {
                    Player? player = Player.m_localPlayer;
                    int recipeCount = 0;
                    int stationCount1 = 0;
                    int stationCount2 = 0;
                    int pieceCount = 0;
                    if (player != null && ObjectDB.instance != null)
                    {
                        foreach (Recipe recipe in ObjectDB.instance.m_recipes)
                        {
                            if (string.IsNullOrWhiteSpace(recipe.m_item?.m_itemData?.m_shared?.m_name)) continue;
                            if (player.m_knownRecipes.Contains(recipe.m_item?.m_itemData?.m_shared?.m_name))
                                continue;
                            player.m_knownRecipes.Add(recipe.m_item?.m_itemData?.m_shared?.m_name);
                            player.UpdateKnownRecipesList();
                            recipeCount++;
                        }

                        foreach (PieceTable pieceTable in Resources.FindObjectsOfTypeAll<PieceTable>())
                        {
                            foreach (GameObject? piece in pieceTable.m_pieces)
                            {
                                Piece? pieceComponent = piece.GetComponent<Piece>();
                                if (pieceComponent == null)
                                    continue;
                                if (string.IsNullOrWhiteSpace(pieceComponent.m_name)) continue;
                                if (player.m_knownRecipes.Contains(pieceComponent.m_name))
                                    continue;
                                player.m_knownRecipes.Add(pieceComponent.m_name);
                                player.UpdateKnownRecipesList();
                                pieceCount++;
                            }
                        }

                        foreach (CraftingStation station in Resources.FindObjectsOfTypeAll<CraftingStation>())
                        {
                            int level = station.GetLevel();
                            int num;
                            if (player.m_knownStations.TryGetValue(station.m_name, out num))
                            {
                                if (num >= level) continue;
                                player.m_knownStations[station.m_name] = level;
                                player.UpdateKnownRecipesList();
                                stationCount1++;
                            }
                            else
                            {
                                if (string.IsNullOrWhiteSpace(station.m_name)) continue;
                                player.m_knownStations.Add(station.m_name, level);
                                player.UpdateKnownRecipesList();
                                stationCount2++;
                            }
                        }

                        // Log how many of each thing was learned
                        IKnowThingsPlugin.IKnowThingsLogger.LogInfo($"Learned {recipeCount} recipes, {pieceCount} pieces, {stationCount1 + stationCount2} stations ({stationCount1} upgraded, {stationCount2} new) and 0 life lessons");
                        args.Context.AddString($"Learned {recipeCount} recipes, {pieceCount} pieces, {stationCount1 + stationCount2} stations ({stationCount1} upgraded, {stationCount2} new) and 0 life lessons");
                    }
                }
                else
                {
                    args.Context.AddString("You must be an admin to use this command");
                }
            });

            Command("unlearnrecipes", "Un-Learns all recipes for your player. Only admins can execute this command", (args) =>
            {
                if (Admin.Enabled)
                {
                    Player? player = Player.m_localPlayer;
                    if (player != null && ObjectDB.instance != null)
                    {
                        player.m_knownRecipes.Clear();
                        player.m_knownStations.Clear();
                        player.m_knownMaterial.Clear();


                        // Log how many of each thing was learned
                        IKnowThingsPlugin.IKnowThingsLogger.LogInfo($"Reset all recipes, pieces, stations and life lessons");
                        args.Context.AddString($"Reset all recipes, pieces, stations and life lessons");
                    }
                }
                else
                {
                    args.Context.AddString("You must be an admin to use this command");
                }
            });
        }

        public static Terminal.ConsoleEvent Catch(Terminal.ConsoleEvent action) =>
            (args) =>
            {
                try
                {
                    action(args);
                }
                catch (InvalidOperationException e)
                {
                    AddError(args.Context, e.Message);
                }
            };

        public static void AddMessage(Terminal context, string message)
        {
            context.AddString(message);
            Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, message);
        }

        public static void AddError(Terminal context, string message)
        {
            AddMessage(context, $"Error: {message}");
        }

        public static void Command(string name, string description, Terminal.ConsoleEvent action, Terminal.ConsoleOptionsFetcher? fetcher = null)
        {
            new Terminal.ConsoleCommand(name, description, Catch(action), isCheat: true, isNetwork: true, optionsFetcher: fetcher);
        }
    }
}