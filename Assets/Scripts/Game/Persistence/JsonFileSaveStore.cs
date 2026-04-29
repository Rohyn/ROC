using System;
using System.IO;
using UnityEngine;

namespace ROC.Persistence
{
    public class JsonFileSaveStore : ISaveStore
    {
        private const int CurrentVersion = 1;

        private readonly string _rootPath;

        public JsonFileSaveStore(string rootPath = null)
        {
            _rootPath = string.IsNullOrWhiteSpace(rootPath)
                ? Path.Combine(Application.persistentDataPath, "Saves")
                : rootPath;
        }

        public bool TryLoadAccount(string accountId, out AccountSaveData account)
        {
            account = null;

            string path = GetAccountPath(accountId);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                SaveGameEnvelope<AccountSaveData> envelope =
                    JsonUtility.FromJson<SaveGameEnvelope<AccountSaveData>>(json);

                if (envelope == null || envelope.Data == null)
                {
                    return false;
                }

                account = envelope.Data;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JsonFileSaveStore] Failed to load account '{accountId}': {ex}");
                return false;
            }
        }

        public void SaveAccount(AccountSaveData account)
        {
            if (account == null || string.IsNullOrWhiteSpace(account.AccountId))
            {
                Debug.LogWarning("[JsonFileSaveStore] Refused to save invalid account data.");
                return;
            }

            SaveEnvelope(GetAccountPath(account.AccountId), account);
        }

        public bool TryLoadCharacter(string characterId, out CharacterSaveData character)
        {
            character = null;

            string path = GetCharacterPath(characterId);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                SaveGameEnvelope<CharacterSaveData> envelope =
                    JsonUtility.FromJson<SaveGameEnvelope<CharacterSaveData>>(json);

                if (envelope == null || envelope.Data == null)
                {
                    return false;
                }

                character = envelope.Data;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JsonFileSaveStore] Failed to load character '{characterId}': {ex}");
                return false;
            }
        }

        public void SaveCharacter(CharacterSaveData character)
        {
            if (character == null || string.IsNullOrWhiteSpace(character.CharacterId))
            {
                Debug.LogWarning("[JsonFileSaveStore] Refused to save invalid character data.");
                return;
            }

            SaveEnvelope(GetCharacterPath(character.CharacterId), character);
        }

        private void SaveEnvelope<T>(string path, T data)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                SaveGameEnvelope<T> envelope = new SaveGameEnvelope<T>
                {
                    Version = CurrentVersion,
                    Data = data
                };

                string json = JsonUtility.ToJson(envelope, true);
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[JsonFileSaveStore] Failed to save '{path}': {ex}");
            }
        }

        private string GetAccountPath(string accountId)
        {
            return Path.Combine(_rootPath, "Accounts", SanitizeFileName(accountId) + ".json");
        }

        private string GetCharacterPath(string characterId)
        {
            return Path.Combine(_rootPath, "Characters", SanitizeFileName(characterId) + ".json");
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "invalid";
            }

            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }

            return value;
        }
    }
}