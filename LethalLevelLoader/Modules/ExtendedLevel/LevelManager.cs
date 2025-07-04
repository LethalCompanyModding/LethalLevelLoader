﻿using LethalFoundation;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem.Utilities;

namespace LethalLevelLoader
{
    public class LevelManager : ExtendedContentManager<ExtendedLevel, SelectableLevel>
    {
        public static ExtendedLevel CurrentExtendedLevel => Refs.CurrentLevel != null ? Refs.CurrentLevel.AsExtended() : null;

        public static LevelEvents GlobalLevelEvents = new LevelEvents();

        public static List<DayHistory> dayHistoryList = new List<DayHistory>();
        public static int daysTotal;
        public static int quotasTotal;

        public static int invalidSaveLevelID = -1;

        public static Dictionary<string, int> dynamicRiskLevelDictionary = new Dictionary<string, int>()
        {
            {"D-", 0},
            {"D", 0},
            {"D+", 0},
            {"C-", 0},
            {"C", 0},
            {"C+", 0},
            {"B-", 0},
            {"B", 0},
            {"B+", 0},
            {"A-", 0},
            {"A", 0},
            {"A+", 0},
            {"S-", 0},
            {"S", 0},
            {"S+", 0},
            {"S++", 0 },
            {"S+++", 0}
        };


        protected override List<SelectableLevel> GetVanillaContent() => new List<SelectableLevel>(StartOfRound.levels);
        protected override ExtendedLevel ExtendVanillaContent(SelectableLevel content)
        {
            ExtendedLevel extendedLevel = ExtendedLevel.Create(content);
            PatchedContent.AllLevelSceneNames.Add(extendedLevel.SelectableLevel.sceneName);
            extendedLevel.name = extendedLevel.NumberlessPlanetName + "ExtendedLevel";
            extendedLevel.IsRouteHidden = Refs.Nodes.MoonsResult.displayText.Contains(extendedLevel.NumberlessPlanetName);
            return (extendedLevel);
        }

        protected override void PatchGame()
        {
            DebugHelper.Log(GetType().Name + " Patching Game!", DebugType.User);

            StartOfRound.levels = ExtendedContents.Select(e => e.SelectableLevel).ToArray();
            Terminal.moonsCatalogueList = ExtendedContents.Where(e => e.IsRouteHidden == false).Select(e => e.SelectableLevel).ToArray();
            foreach (ExtendedLevel level in ExtendedContents)
            {
                level.SetGameID(StartOfRound.levels.IndexOf(level.SelectableLevel));
                if (level is ITerminalEntry terminalEntry)
                    terminalEntry.TryRegister();
            }
        }

        protected override void UnpatchGame()
        {
            DebugHelper.Log(GetType().Name + " Unpatching Game!", DebugType.User);
        }

        internal static void InitializeShipAnimatorOverrideController()
        {
            Animator shipAnimator = Refs.StartOfRound.shipAnimator;

            List<GameObject> childObjects = new List<GameObject>();
            foreach (Transform child in shipAnimator.GetComponentsInChildren<Transform>(includeInactive: true))
                if (!childObjects.Contains(child.gameObject))
                    childObjects.Add(child.gameObject);

            AnimatorOverrideController overrideController = new AnimatorOverrideController(shipAnimator.runtimeAnimatorController);
            LevelLoader.shipAnimatorOverrideController = overrideController;
            LevelLoader.defaultShipFlyToMoonClip = overrideController["HangarShipLandB"];
            LevelLoader.defaultShipFlyFromMoonClip = overrideController["ShipLeave"];

            foreach (AnimationClip animationClip in shipAnimator.runtimeAnimatorController.animationClips)
                overrideController[animationClip.name] = animationClip;

            shipAnimator.runtimeAnimatorController = overrideController;
            //shipAnimator.Play("Base Layer.ShipIdle", layer: 0, normalizedTime: 1.0f);

            //shipAnimator.enabled = false;
        }

        public static bool TryGetExtendedLevel(SelectableLevel selectableLevel, out ExtendedLevel returnExtendedLevel, ContentType levelType = ContentType.Any)
        {
            returnExtendedLevel = GetExtendedLevel(selectableLevel);
            return (returnExtendedLevel != null);
        }

        public static ExtendedLevel GetExtendedLevel(SelectableLevel selectableLevel)
        {
            return (selectableLevel != null ? selectableLevel.AsExtended() : null);
        }

        public static void PopulateDynamicRiskLevelDictionary()
        {
            Dictionary<string, List<int>> vanillaRiskLevelDictionary = new Dictionary<string, List<int>>();

            foreach (ExtendedLevel vanillaLevel in PatchedContent.VanillaExtendedLevels)
            {
                string risk = vanillaLevel.SelectableLevel.riskLevel;
                DebugHelper.Log("Risk Level Of " + vanillaLevel.NumberlessPlanetName + " Is: " + risk, DebugType.Developer);
                if (string.IsNullOrEmpty(risk) || risk.Contains("Safe")) continue;
                vanillaRiskLevelDictionary.AddOrAddAdd(risk, vanillaLevel.CalculatedDifficultyRating);
            }

            foreach (KeyValuePair<string, List<int>> vanillaRiskLevel in vanillaRiskLevelDictionary)
            {
                string debugString = "Vanilla Risk Level Group (" + vanillaRiskLevel.Key + "): ";
                if (vanillaRiskLevel.Value != null)
                {
                    debugString += " Average - " + vanillaRiskLevel.Value.Average() + ", Values - ";
                    foreach (int calculatedDifficulty in vanillaRiskLevel.Value)
                        debugString += calculatedDifficulty.ToString() + ", ";
                }
                DebugHelper.Log(debugString, DebugType.Developer);
            }

            foreach (KeyValuePair<string, int> dynamicRiskLevelPair in new Dictionary<string, int>(dynamicRiskLevelDictionary))
                    foreach (KeyValuePair<string, List<int>> vanillaRiskLevel in vanillaRiskLevelDictionary)
                        if (dynamicRiskLevelPair.Key.Equals(vanillaRiskLevel.Key))
                        {
                            DebugHelper.Log("Setting RiskLevel " + vanillaRiskLevel.Key + " To " + (int)vanillaRiskLevel.Value.Average(), DebugType.Developer);
                            dynamicRiskLevelDictionary[dynamicRiskLevelPair.Key] = Mathf.RoundToInt((float)vanillaRiskLevel.Value.Average());
                        }

            DebugHelper.Log("Starting To Assign - and + Risk Levels", DebugType.Developer);
            int counter = 0;
            foreach (KeyValuePair<string, int> dynamicRiskLevelPair in new Dictionary<string, int>(dynamicRiskLevelDictionary))
            {
                string previousFullRiskLevel = string.Empty;
                string currentFullRiskLevel = string.Empty;
                string nextFullRiskLevel = string.Empty;
                DebugHelper.Log("Trying To Assign Value To Risk Level: " + dynamicRiskLevelPair.Key, DebugType.Developer);

                if (dynamicRiskLevelPair.Key.Contains("-"))
                {
                    if (counter != 0)
                        previousFullRiskLevel = dynamicRiskLevelDictionary.Keys.ToList()[counter - 2];
                    currentFullRiskLevel = dynamicRiskLevelDictionary.Keys.ToList()[counter + 1];

                    if (counter == 0)
                        dynamicRiskLevelDictionary[dynamicRiskLevelPair.Key] = (dynamicRiskLevelDictionary[currentFullRiskLevel] / 2);
                    else
                        dynamicRiskLevelDictionary[dynamicRiskLevelPair.Key] = Mathf.RoundToInt(Mathf.Lerp(dynamicRiskLevelDictionary[previousFullRiskLevel], dynamicRiskLevelDictionary[currentFullRiskLevel], 0.66f));

                }
                else if (dynamicRiskLevelPair.Key.Contains("+") && !dynamicRiskLevelPair.Key.Equals("S+"))
                {
                    currentFullRiskLevel = dynamicRiskLevelDictionary.Keys.ToList()[counter - 1];
                    if (!dynamicRiskLevelPair.Key.Contains("S"))
                        nextFullRiskLevel = dynamicRiskLevelDictionary.Keys.ToList()[counter + 2];

                    if (dynamicRiskLevelPair.Key.Equals("S++"))
                        dynamicRiskLevelDictionary[dynamicRiskLevelPair.Key] = (dynamicRiskLevelDictionary[currentFullRiskLevel] * 2);
                    else if (dynamicRiskLevelPair.Key.Equals("S+++"))
                        dynamicRiskLevelDictionary[dynamicRiskLevelPair.Key] = (dynamicRiskLevelDictionary[currentFullRiskLevel] * 3);
                    else
                        dynamicRiskLevelDictionary[dynamicRiskLevelPair.Key] = Mathf.RoundToInt(Mathf.Lerp(dynamicRiskLevelDictionary[currentFullRiskLevel], dynamicRiskLevelDictionary[nextFullRiskLevel], 0.33f));
                }

                DebugHelper.Log("Risk Level: " + dynamicRiskLevelPair.Key + " Was Assigned Calculated Difficulty Of: " + dynamicRiskLevelDictionary[dynamicRiskLevelPair.Key].ToString(), DebugType.Developer);
                counter++;
            }

            foreach (KeyValuePair<string, int> dynamicRiskLevelPair in new Dictionary<string, int>(dynamicRiskLevelDictionary))
                DebugHelper.Log("Dynamic Risk Level Pair: " + dynamicRiskLevelPair.Key + " (" + dynamicRiskLevelPair.Value + ")", DebugType.Developer);
        }

        public static void AssignCalculatedRiskLevels()
        {
            Dictionary<int, string> assignmentRiskLevelDictionary = new Dictionary<int, string>();
            List<int> orderedCalculatedDifficultyList = new List<int>(dynamicRiskLevelDictionary.Values);
            orderedCalculatedDifficultyList.Sort();

            foreach (int calculatedDifficultyValue in orderedCalculatedDifficultyList)
                if (!orderedCalculatedDifficultyList.Contains(calculatedDifficultyValue))
                    foreach (KeyValuePair<string, int> calculatedRiskLevel in dynamicRiskLevelDictionary)
                        if (calculatedRiskLevel.Value == calculatedDifficultyValue)
                            assignmentRiskLevelDictionary.Add(calculatedDifficultyValue, calculatedRiskLevel.Key);

            foreach (KeyValuePair<int, string> calculatedRiskLevel in assignmentRiskLevelDictionary)
                DebugHelper.Log("Ordered Calculated Risk Level: (" + calculatedRiskLevel.Value + ") - " + calculatedRiskLevel.Key, DebugType.Developer);

            foreach (ExtendedLevel customLevel in PatchedContent.CustomExtendedLevels)
            {
                if (customLevel.OverrideDynamicRiskLevelAssignment == false)
                {
                    int customLevelCalculatedDifficultyRating = customLevel.CalculatedDifficultyRating;
                    int closestCalculatedRiskLevelRating = orderedCalculatedDifficultyList[0];

                    closestCalculatedRiskLevelRating = orderedCalculatedDifficultyList.OrderBy(item => Math.Abs(customLevelCalculatedDifficultyRating - item)).First();

                    if (closestCalculatedRiskLevelRating != 0)
                        customLevel.SelectableLevel.riskLevel = assignmentRiskLevelDictionary[closestCalculatedRiskLevelRating];
                }
            }

            List<ExtendedLevel> extendedLevelsOrdered = new List<ExtendedLevel>(PatchedContent.ExtendedLevels).OrderBy(o => o.CalculatedDifficultyRating).ToList();

            foreach (ExtendedLevel extendedLevel in extendedLevelsOrdered)
                DebugHelper.Log(extendedLevel.NumberlessPlanetName + " (" + extendedLevel.SelectableLevel.riskLevel + ") " + " (" + extendedLevel.CalculatedDifficultyRating + ")", DebugType.Developer);
        }

        public static void LogDayHistory()
        {
            //Heavy early returns here because this runs from a DunGen patch and needs to be safe for unconventional Unity-Editor generation usage.
            if (Plugin.IsSetupComplete == false || Refs.StartOfRound == null || Refs.RoundManager == null || Refs.TimeOfDay == null)
            {
                DebugHelper.LogWarning("Game Seems Uninitialized, Exiting LogDayHistory Early!", DebugType.Developer);
                return;
            }

            DayHistory newDay = new DayHistory();
            daysTotal++;

            newDay.allViableOptions = DungeonManager.GetValidExtendedDungeonFlows(CurrentExtendedLevel, false).Select(e => e.extendedDungeonFlow).ToList();
            newDay.extendedLevel = LevelManager.CurrentExtendedLevel;
            newDay.extendedDungeonFlow = DungeonManager.CurrentExtendedDungeonFlow;
            newDay.day = daysTotal;
            newDay.quota = Refs.TimesFulfilledQuota;
            newDay.weatherEffect = Refs.CurrentWeather;

            string debugString = "Created New Day History Log! PlanetName: ";
            debugString += newDay.extendedLevel != null ? newDay.extendedLevel.NumberlessPlanetName + " ," : "MISSING EXTENDEDLEVEL ,";
            debugString += newDay.extendedDungeonFlow != null ? newDay.extendedDungeonFlow.DungeonName + " ," : "MISSING EXTENDEDDUNGEONFLOW ,";
            debugString += "Quota: " + newDay.quota + " , Day: " + newDay.day + " , Weather: " + newDay.weatherEffect.ToString();

            DebugHelper.Log(debugString, DebugType.User);

            if (dayHistoryList == null)
                dayHistoryList = new List<DayHistory>();

            dayHistoryList.Add(newDay);
        }

        public static int CalculateExtendedLevelDifficultyRating(ExtendedLevel extendedLevel, bool debugResults = false)
        {
            int returnRating = 0;
            int baselineRouteValue = extendedLevel.PurchasePrice;
            baselineRouteValue += extendedLevel.SelectableLevel.maxTotalScrapValue;
            returnRating += baselineRouteValue;
            int scrapValue = 0;
            foreach (SpawnableItemWithRarity spawnableScrap in extendedLevel.SelectableLevel.spawnableScrap)
            {
                if (spawnableScrap.spawnableItem != null)
                {
                    if (((spawnableScrap.spawnableItem.minValue + spawnableScrap.spawnableItem.maxValue) * 5) != 0 && spawnableScrap.rarity != 0)
                    {
                        if ((spawnableScrap.rarity / 10) != 0)
                            scrapValue += (spawnableScrap.spawnableItem.maxValue - spawnableScrap.spawnableItem.minValue) / (spawnableScrap.rarity / 10);
                    }
                }
            }
            returnRating += scrapValue;
            int enemySpawnValue = (extendedLevel.SelectableLevel.maxEnemyPowerCount + extendedLevel.SelectableLevel.maxOutsideEnemyPowerCount + extendedLevel.SelectableLevel.maxDaytimeEnemyPowerCount) * 15;
            enemySpawnValue = enemySpawnValue * 2;
            returnRating += enemySpawnValue;
            float enemyValue = 0;
            foreach (SpawnableEnemyWithRarity spawnableEnemy in extendedLevel.SelectableLevel.Enemies.Concat(extendedLevel.SelectableLevel.OutsideEnemies).Concat(extendedLevel.SelectableLevel.DaytimeEnemies))
            {
                if (spawnableEnemy.rarity != 0 && spawnableEnemy.enemyType != null)
                    if ((spawnableEnemy.rarity / 10) != 0)
                        enemyValue += (spawnableEnemy.enemyType.PowerLevel * 100) / (spawnableEnemy.rarity / 10);
            }
            returnRating += Mathf.RoundToInt(enemyValue);
            returnRating += Mathf.RoundToInt(returnRating * (extendedLevel.SelectableLevel.factorySizeMultiplier * 0.5f));
            return (returnRating);
        }

        protected override (bool result, string log) ValidateExtendedContent(ExtendedLevel content)
        {
            if (string.IsNullOrEmpty(content.SelectableLevel.sceneName))
                return ((false, "SelectableLevel SceneName Was Null Or Empty"));
            else if (content.SelectableLevel.planetPrefab == null)
                return ((false, "SelectableLevel PlanetPrefab Was Null"));
            else if (content.SelectableLevel.planetPrefab.GetComponent<Animator>() == null)
                return ((false, "SelectableLevel PlanetPrefab Animator Was Null"));
            else if (content.SelectableLevel.planetPrefab.GetComponent<Animator>().runtimeAnimatorController == null)
                return ((false, "SelectableLevel PlanetPrefab Animator AnimatorController Was Null"));
            else
                return (true, string.Empty);
        }

        protected override void PopulateContentTerminalData(ExtendedLevel content)
        {
            TerminalKeyword keyword = null;
            TerminalNode routeNode = null;
            TerminalNode routeConfirmNode = null;
            TerminalNode routeInfoNode = null;

            if (Keywords.Route.compatibleNouns.Length > content.GameID)
            {
                foreach (CompatibleNoun noun in Keywords.Route.compatibleNouns)
                    if (noun.result.displayPlanetInfo == content.GameID)
                    {
                        keyword = noun.noun;
                        routeNode = noun.result;
                        routeConfirmNode = routeNode.terminalOptions[1].result;
                        content.SetPurchasePrice(routeNode.itemCost);  //This should not be here but it's difficult to find a more appropiate spot rn
                        break;
                    }
                if (Keywords.Info.compatibleNouns.TryGet(keyword, out TerminalNode result))
                    routeInfoNode = result;  
            }
            else
            {
                string sanitisedName = content.NumberlessPlanetName.StripSpecialCharacters().Sanitized();
                keyword = Terminal.CreateKeyword(sanitisedName + "Keyword", content.TerminalNoun, Keywords.Route);

                routeNode = Terminal.CreateNode(sanitisedName + "Route");
                if (content.OverrideRouteNodeDescription != string.Empty)
                    routeNode.displayText = content.OverrideRouteNodeDescription;
                else
                {
                    routeNode.displayText = "The cost to route to " + content.SelectableLevel.PlanetName + " is [totalCost]. It is currently [currentPlanetTime] on this moon.";
                    routeNode.displayText += "\n" + "\n" + "Please CONFIRM or DENY." + "\n" + "\n";
                }
                routeNode.clearPreviousText = true;
                routeNode.buyRerouteToMoon = -2;
                routeNode.itemCost = content.PurchasePrice;
                routeNode.overrideOptions = true;

                routeConfirmNode = Terminal.CreateNode(sanitisedName + "RouteConfirm");
                if (content.OverrideRouteConfirmNodeDescription != string.Empty)
                    routeConfirmNode.displayText = content.OverrideRouteConfirmNodeDescription;
                else
                    routeConfirmNode.displayText = "Routing autopilot to " + content.SelectableLevel.PlanetName + " Your new balance is [playerCredits]. \n\nPlease enjoy your flight.";
                routeConfirmNode.clearPreviousText = true;
                routeConfirmNode.itemCost = content.PurchasePrice;

                routeInfoNode = Terminal.CreateNode(sanitisedName + "Info");
                routeInfoNode.clearPreviousText = true;
                routeInfoNode.maxCharactersToType = 35;
                string infoString;
                if (content.OverrideInfoNodeDescription != string.Empty)
                    infoString = content.OverrideInfoNodeDescription;
                else
                {
                    infoString = content.SelectableLevel.PlanetName + "\n" + "----------------------" + "\n";
                    List<string> selectableLevelLines = new List<string>();
                    string inputString = content.SelectableLevel.LevelDescription;
                    while (inputString.Contains("\n"))
                    {
                        string inputStringWithoutTextBeforeFirstComma = inputString.Substring(inputString.IndexOf("\n"));
                        selectableLevelLines.Add(inputString.Replace(inputStringWithoutTextBeforeFirstComma, ""));
                        if (inputStringWithoutTextBeforeFirstComma.Contains("\n"))
                            inputString = inputStringWithoutTextBeforeFirstComma.Substring(inputStringWithoutTextBeforeFirstComma.IndexOf("\n") + 1);
                    }
                    selectableLevelLines.Add(inputString);
                    foreach (string line in selectableLevelLines)
                        infoString += "\n" + line + "\n";
                }
                routeInfoNode.displayText = infoString;

                routeNode.AddNoun(Keywords.Deny, Nodes.CancelRoute);
                routeNode.AddNoun(Keywords.Confirm, routeConfirmNode);
            }

            content.PurchasePromptNode = routeNode;
            content.PurchaseConfirmNode = routeConfirmNode;
            content.InfoNode = routeInfoNode;
            content.NounKeyword = keyword;
        }
    }

    public class DayHistory
    {
        public int quota;
        public int day;
        public ExtendedLevel extendedLevel;
        public List<ExtendedDungeonFlow> allViableOptions;
        public ExtendedDungeonFlow extendedDungeonFlow;
        public LevelWeatherType weatherEffect;
    }
}