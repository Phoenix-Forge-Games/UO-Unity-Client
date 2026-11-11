using System.Runtime.CompilerServices;

// The release pipeline encrypts the content pack with the same compiled key the player decrypts with.
[assembly: InternalsVisibleTo("UOClient.Editor")]
// Per-bundle encryption of the Protected_* Addressables groups (Docs/SecureArt.md) uses the same key.
[assembly: InternalsVisibleTo("UOClient.SecureArt.Addressables")]
[assembly: InternalsVisibleTo("UOClient.SecureArt.Editor")]
