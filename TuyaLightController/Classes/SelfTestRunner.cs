using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace TuyaLightController {
    internal static class SelfTestRunner {
        public static int Run() {
            int passed = 0;
            int failed = 0;

            RunCase("ScalePercent_25_255", () => {
                AssertEqual(25, ScaleUtil.ScalePercent(0, 25, 255), "0%");
                AssertEqual(140, ScaleUtil.ScalePercent(50, 25, 255), "50%");
                AssertEqual(255, ScaleUtil.ScalePercent(100, 25, 255), "100%");
            }, ref passed, ref failed);

            RunCase("ScalePercent_10_1000", () => {
                AssertEqual(10, ScaleUtil.ScalePercent(0, 10, 1000), "0%");
                AssertEqual(505, ScaleUtil.ScalePercent(50, 10, 1000), "50%");
                AssertEqual(1000, ScaleUtil.ScalePercent(100, 10, 1000), "100%");
            }, ref passed, ref failed);

            RunCase("ScalePercent_Clamp", () => {
                AssertEqual(0, ScaleUtil.ScalePercent(-10, 0, 1000), "below range");
                AssertEqual(1000, ScaleUtil.ScalePercent(150, 0, 1000), "above range");
            }, ref passed, ref failed);

            RunCase("GlobalSettings_ApiTokenOptional", () => {
                var settings = new GlobalSettings { ApiToken = "" };
                settings.Normalize();
                AssertEqual("", settings.ApiToken, "empty local token stays empty");
            }, ref passed, ref failed);

            RunCase("LightSpec_DefaultAndOverride", () => {
                var djv1 = LightSpec.For(new TuyaLight { Category = "dj", V2 = false });
                AssertEqual("bright_value", djv1.BrightnessCode, "dj v1 brightness code");
                AssertEqual(25, djv1.BrightnessMin, "dj v1 brightness min");

                var dd = LightSpec.For(new TuyaLight { Category = "dd", V2 = false });
                AssertEqual(10, dd.BrightnessMin, "dd brightness min");
                AssertEqual(1000, dd.BrightnessMax, "dd brightness max");

                var custom = LightSpec.For(new TuyaLight {
                    Category = "dj",
                    V2 = false,
                    Capabilities = new TuyaLightCapabilities {
                        BrightnessCode = "custom_brightness",
                        BrightnessMin = 1,
                        BrightnessMax = 99,
                        TempCode = "custom_temp",
                        TempMin = 2,
                        TempMax = 88,
                        ColorCode = "custom_color",
                        ColorSatMax = 777,
                        ColorValMax = 666
                    }
                });
                AssertEqual("custom_brightness", custom.BrightnessCode, "override brightness code");
                AssertEqual(1, custom.BrightnessMin, "override brightness min");
            }, ref passed, ref failed);

            RunCase("LightCapabilities_SchemaRanges", () => {
                var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                    "switch_led",
                    "work_mode",
                    "bright_value_v2",
                    "temp_value_v2",
                    "colour_data_v2"
                };
                var schemas = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase) {
                    ["bright_value_v2"] = JObject.Parse("{\"min\":10,\"max\":1000}"),
                    ["temp_value_v2"] = JObject.Parse("{\"min\":100,\"max\":900}"),
                    ["colour_data_v2"] = JObject.Parse("{\"h\":{\"max\":360},\"s\":{\"max\":1000},\"v\":{\"max\":1000}}")
                };
                var caps = TuyaCloudClient.BuildLightCapabilities(
                    new TuyaLight { Category = "dj", V2 = false },
                    codes,
                    schemas);
                AssertEqual("bright_value_v2", caps.BrightnessCode, "schema brightness code");
                AssertEqual(10, caps.BrightnessMin, "schema brightness min");
                AssertEqual(1000, caps.BrightnessMax, "schema brightness max");
                AssertEqual(100, caps.TempMin, "schema temp min");
                AssertEqual(900, caps.TempMax, "schema temp max");
                AssertEqual(1000, caps.ColorSatMax, "schema color sat max");
            }, ref passed, ref failed);

            RunCase("ReadSwitchState", () => {
                var status = JArray.Parse("[{\"code\":\"switch_led\",\"value\":true},{\"code\":\"temp_value\",\"value\":128}]");
                AssertEqual(true, StatusUtil.ReadSwitchState(status, "switch_led"), "known code");
                AssertEqual(null, StatusUtil.ReadSwitchState(status, "missing"), "missing code");
            }, ref passed, ref failed);

            RunCase("SceneDispatchPlanner_Matrix", () => {
                var slugs = new[] { "plug-1", "light-a", "light-b", "light-c" };
                var statuses = new Dictionary<string, TuyaDeviceStatus>(StringComparer.OrdinalIgnoreCase) {
                    ["plug-1"] = new TuyaDeviceStatus { Reachable = true, State = false, IsLight = false },
                    ["light-a"] = new TuyaDeviceStatus { Reachable = true, State = false, IsLight = true },
                    ["light-b"] = new TuyaDeviceStatus { Reachable = true, State = true, IsLight = true },
                    ["light-c"] = new TuyaDeviceStatus { Reachable = false, State = null, IsLight = true }
                };
                var plan = SceneDispatchPlanner.Build(slugs, statuses, TuyaApiClient.IsLight);
                AssertEqual(1, plan.PlugOnSlugs.Count, "plug on");
                AssertEqual(1, plan.OffLightSlugs.Count, "off lights");
                AssertEqual(1, plan.OnLightSlugs.Count, "on lights");
                AssertEqual(1, plan.OfflineSlugs.Count, "offline");
            }, ref passed, ref failed);

            RunCase("ToggleDecision_Matrix", () => {
                var statusesAllOn = new Dictionary<string, TuyaDeviceStatus>(StringComparer.OrdinalIgnoreCase) {
                    ["a"] = new TuyaDeviceStatus { Reachable = true, State = true },
                    ["b"] = new TuyaDeviceStatus { Reachable = true, State = true }
                };
                AssertEqual(false, ToggleDecision.ShouldTurnOn(new[] { "a", "b" }, statusesAllOn), "all on -> turn off");

                var statusesMixed = new Dictionary<string, TuyaDeviceStatus>(StringComparer.OrdinalIgnoreCase) {
                    ["a"] = new TuyaDeviceStatus { Reachable = true, State = true },
                    ["b"] = new TuyaDeviceStatus { Reachable = true, State = false }
                };
                AssertEqual(true, ToggleDecision.ShouldTurnOn(new[] { "a", "b" }, statusesMixed), "mixed -> turn on");
            }, ref passed, ref failed);

            Console.WriteLine("Self tests complete. Passed: " + passed + ", Failed: " + failed);
            return failed == 0 ? 0 : 1;
        }

        private static void RunCase(string name, Action test, ref int passed, ref int failed) {
            try {
                test();
                passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex) {
                failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string label) {
            if (!Equals(expected, actual)) {
                throw new InvalidOperationException(label + " expected=" + expected + " actual=" + actual);
            }
        }
    }
}
