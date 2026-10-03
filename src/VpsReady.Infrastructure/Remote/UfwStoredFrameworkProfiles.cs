namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// Fingerprints, not embedded upstream code. Reproduce with
/// eng/verify-ufw-upstream.py: UFW 0.36.2 initial templates and complete backend
/// writer, default input/forward DROP and output ACCEPT, logging off through
/// full, with/without per-family limit capability. Unknown profiles fail closed.
/// </summary>
internal static class UfwStoredFrameworkProfiles
{
    private static readonly HashSet<string> Ipv4 = new(StringComparer.Ordinal)
    {
        "3436e453fc0b10ecc93419e0b92847d6b4d350d23860014f33e123eaaeaccedc", // initial
        "e614e7132b927314f1c7d02464eab05a0cecacf665dae1268fcad9e07b473960", // off/no-limit
        "f95fc0ece94ca78cbd0e539567d1799e9f884b81e5b70c0d682ad26c51cdae42", // low/no-limit
        "d07417f91b050b881194374c343adbcdae698c47c76a82934bb232bb9a27e3ee", // medium/no-limit
        "54d8004dac6a2216da7003f8ae352937af0affb733422d56375fd996966a6080", // high/no-limit
        "96f10c18f682565ddbdb8ca00fbd7275fc3e9a8748d97724345e912abfb83981", // full/no-limit
        "9abe1e543a1e9d02feccd7f733784ad44227906c3d11667dd95b8febae81eb6c", // off/limit
        "0e3cac834aaf660057a73a9dda21e17b24d4ac2c82ab3d1ca376117a8418970c", // low/limit
        "d3aafeb38fa77ea93e75a5369ad8a0299c4dd9324afcc0d6b5961eff83c008e8", // medium/limit
        "3f21406a2da0407b503fe8b64e31c1371f8fda508259dbc31b32345867a26a94", // high/limit
        "7b6471eef0b9f80ecf17c7504876551464d542588ece3142da92d5f5f2df55d1", // full/limit
    };

    private static readonly HashSet<string> Ipv6 = new(StringComparer.Ordinal)
    {
        "274fa5e9a9cad60d36d0da6a31186624b232bbaffc17754db606739fa6661d44", // initial
        "4e4c0ec3aa71c834e2553bcadb6bb4c5bba917f616d29303b1d5a9902d49d837", // off/no-limit
        "03f2808cb3f369794dfb2f16933dc74f5c466016a65dff8dfdb207243e5b29a6", // low/no-limit
        "43b9074af0bdd7db9818b946d5691cfad8529a92b8d7a1fcd02d093f66fc9292", // medium/no-limit
        "6a9c90c4fb6b810bae6c010e9069d814293126efed2ab0daba5b8aec4befa915", // high/no-limit
        "c37964512ffa49e4e75ef8869d9b22d31cea8db1249b7e5f47c6a90259c6889b", // full/no-limit
        "ba8650ccc62ecfafd52d17fc7a05b0e5675943b7819310b71d7cf3632e7792c4", // off/limit
        "35f360cf50f56807ae35640e1810cda6553728486ac802c0a96f6cc0cd62e6bb", // low/limit
        "3d4eb78ab60ae661a26db46d93dcb678e7eb564e752832637452617bce81fa03", // medium/limit
        "15605f7ae709ffaae626e085634de4ae2444cb605fe4d1dc5a710a401c42ac58", // high/limit
        "3ab3f5d0650d6f6b2fc9c7b76b38193aa347056847d4f994ab717b565ffed6ab", // full/limit
    };

    internal static bool Contains(bool ipv6, string digest) => (ipv6 ? Ipv6 : Ipv4).Contains(digest);
}
