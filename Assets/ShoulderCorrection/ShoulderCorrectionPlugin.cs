using Warudo.Core.Attributes;
using Warudo.Core.Plugins;

[PluginType(Id = "surfacewetness.shoulder-correction", Name = "ShoulderCorrection", Version = "0.1.1",
    Author = "wonwoo", Description = "Procedural shoulder assistance for animation and tracked hands.",
    AssetTypes = new[] { typeof(ShoulderCorrectionAsset) })]
public class ShoulderCorrectionPlugin : Plugin { }
