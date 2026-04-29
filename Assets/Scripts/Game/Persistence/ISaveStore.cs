namespace ROC.Persistence
{
    public interface ISaveStore
    {
        bool TryLoadAccount(string accountId, out AccountSaveData account);
        void SaveAccount(AccountSaveData account);

        bool TryLoadCharacter(string characterId, out CharacterSaveData character);
        void SaveCharacter(CharacterSaveData character);
    }
}