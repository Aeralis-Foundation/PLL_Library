using PLL.Library;
using UnityEngine;

namespace PLL.Creator.Editor
{
    public sealed class PllEnvironmentPreset : ScriptableObject
    {
        [SerializeField] PllBiomeDefinition SourceDefinition;

        public PllBiomeDefinition Source => SourceDefinition;

        internal void Configure(PllBiomeDefinition Source) => SourceDefinition = Source;
    }
}
