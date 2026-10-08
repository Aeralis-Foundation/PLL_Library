using UnityEngine;

namespace PLL.Library;

[DisallowMultipleComponent]
[AddComponentMenu("PLL/Props/Campfire")]
public sealed class PllCampfireAuthoring : MonoBehaviour
{
    [SerializeField] GameObject root = null!;
    [SerializeField] GameObject modelRoot = null!;
    [SerializeField] ParticleSystem fireParticles = null!;
    [SerializeField] ParticleSystem smokeParticlesOff = null!;
    [SerializeField] ParticleSystem smokeParticlesLit = null!;
    [SerializeField] Transform logRoot = null!;
    [SerializeField] GameObject? enableWhenLit = null;
    [SerializeField] GameObject? disableWhenLit = null;
    [SerializeField] AudioSource? loop = null;

    public GameObject Root => root;
    public GameObject ModelRoot => modelRoot;
    public ParticleSystem FireParticles => fireParticles;
    public ParticleSystem SmokeParticlesOff => smokeParticlesOff;
    public ParticleSystem SmokeParticlesLit => smokeParticlesLit;
    public Transform LogRoot => logRoot;
    public GameObject? EnableWhenLit => enableWhenLit;
    public GameObject? DisableWhenLit => disableWhenLit;
    public AudioSource? Loop => loop;
}
