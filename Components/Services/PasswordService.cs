using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using System;
using CredentialManagement;
using nump.Components.Classes;


public class PasswordService
{
    public static List<string> wordList = new List<string>
    {
        "apple", "banana", "cherry", "dog", "elephant", "fox", "grape", "hat", "island", "jungle",
        "kite", "lemon", "mountain", "notebook", "orange", "peach", "quilt", "rabbit", "snake", "tiger",
        "adventure", "balloon", "cake", "dolphin", "eagle", "fountain", "glove", "horizon", "infinity", "jet",
        "kangaroo", "lighthouse", "mango", "novel", "octopus", "paradise", "quicksilver", "rocket", "sunflower", "tree",
        "unicorn", "violet", "whale", "xylophone", "yarn", "zebra", "apricot", "bear", "cloud", "daisy",
        "elephant", "forest", "guitar", "harbor", "illusion", "jungle", "kiwi", "lightning", "maple", "night",
        "olive", "pebble", "queen", "rainbow", "starfish", "tornado", "underwater", "vortex", "whisper", "xenon",
        "yellowstone", "zeppelin", "anchor", "breeze", "canyon", "desert", "echo", "flame", "gale", "horizon",
        "iridescent", "journey", "kiwi", "landscape", "moonlight", "nebula", "oasis", "puzzle", "quake", "revolution",
        "snowflake", "tiger", "ufo", "voyage", "whirlpool", "xeno", "yacht", "zenith", "asteroid", "bubble",
        "cactus", "dust", "echo", "frost", "garnet", "horizon", "illusion", "jade", "kaleidoscope", "lava",
        "mermaid", "notch", "ocean", "paradox", "quasar", "ray", "sphere", "tundra", "umbrella", "vortex",
        "wonder", "xenial", "yellow", "zodiac"
    };
    // Use a cryptographically secure random number generator
    private static readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

    // Method to generate a passphrase based on the given parameters
    public string GeneratePassphrase(PWCreationOptions passwordOptions)
    {
        // Initialize the passphrase
        var passphrase = new StringBuilder();

        // Determine the number of words for the passphrase
        int numberOfWords = GetRandomNumber(passwordOptions.minLength, passwordOptions.maxLength + 1);

        // List to store the words before applying any transformations
        var words = new List<string>();

        // Generate the passphrase words (unmodified at first)
        for (int i = 0; i < numberOfWords; i++)
        {
            // Pick a random word from the list
            words.Add(wordList[GetRandomNumber(0, wordList.Count)]);
        }

        // Apply uppercase to exactly `passwordOptions.uppercase` random words
        int uppercaseCount = passwordOptions.uppercase;
        var randomIndexes = new HashSet<int>();

        // Randomly select the indices for words to be made uppercase
        while (uppercaseCount > 0)
        {
            int randomIndex = GetRandomNumber(0, words.Count);
            if (!randomIndexes.Contains(randomIndex))
            {
                words[randomIndex] = words[randomIndex].ToUpper();  // Apply uppercase
                randomIndexes.Add(randomIndex);  // Mark this index as used
                uppercaseCount--;  // Decrease the available uppercase count
            }
        }

        // Build the passphrase from the modified words
        for (int i = 0; i < words.Count; i++)
        {
            passphrase.Append(words[i]);

            // If not the last word, add a separator (string type)
            if (i < words.Count - 1)
            {
                passphrase.Append(passwordOptions.separator);
            }
        }

        // Optionally add special characters or numbers based on the selected amount
        if (passwordOptions.specialChars > 0 || passwordOptions.numbers > 0)
        {
            string specialChars = "@!#*?&";
            string numbers = "0123456789";
            var charPool = new StringBuilder();

            // Add special characters to pool if allowed
            if (passwordOptions.specialChars > 0)
            {
                charPool.Append(specialChars);
            }

            // Add numbers to pool if allowed
            if (passwordOptions.numbers > 0)
            {
                charPool.Append(numbers);
            }

            // Add a random special character or number at the end based on the selected amounts
            while (passwordOptions.specialChars > 0 || passwordOptions.numbers > 0)
            {
                if (passwordOptions.specialChars > 0 && GetRandomNumber(0, 2) == 0)
                {
                    passphrase.Append(charPool[GetRandomNumber(0, specialChars.Length)]);
                    passwordOptions.specialChars--;
                }
                else if (passwordOptions.numbers > 0)
                {
                    passphrase.Append(charPool[GetRandomNumber(specialChars.Length, charPool.Length)]);
                    passwordOptions.numbers--;
                }
            }
        }

        // Return the generated passphrase
        return passphrase.ToString();
    }

    // Helper method to generate cryptographically secure random numbers
    private static int GetRandomNumber(int minValue, int maxValue)
    {
        byte[] randomBytes = new byte[4];
        _rng.GetBytes(randomBytes);
        int randomValue = BitConverter.ToInt32(randomBytes, 0);
        return Math.Abs(randomValue % (maxValue - minValue)) + minValue;
    }
    public string GeneratePassword(PWCreationOptions passwordOptions)
    {
        // Define character sets
        string lowerCaseLetters = "abcdefghijklmnopqrstuvwxyz";
        string upperCaseLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        string numbers = "0123456789";
        string specialChars = "!@#$%^&*()_+-=[]{}|;:,.<>?/";

        // Build the character pool
        var charPool = new StringBuilder(lowerCaseLetters);

        // Create lists to store the required characters
        var passwordChars = new List<char>();

        // Add the required number of uppercase, numbers, and special characters
        for (int i = 0; i < passwordOptions.uppercase; i++)
        {
            passwordChars.Add(upperCaseLetters[GetRandomNumber(0, upperCaseLetters.Length)]);
        }

        for (int i = 0; i < passwordOptions.numbers; i++)
        {
            passwordChars.Add(numbers[GetRandomNumber(0, numbers.Length)]);
        }

        for (int i = 0; i < passwordOptions.specialChars; i++)
        {
            passwordChars.Add(specialChars[GetRandomNumber(0, specialChars.Length)]);
        }

        // Add the remaining characters from the character pool
        int remainingLength = GetRandomNumber(passwordOptions.minLength, passwordOptions.maxLength + 1) - passwordChars.Count;
        for (int i = 0; i < remainingLength; i++)
        {
            passwordChars.Add(charPool[GetRandomNumber(0, charPool.Length)]);
        }

        // Shuffle the password characters to ensure randomness
        var randomPassword = passwordChars.OrderBy(x => GetRandomNumber(0, 100)).ToList();

        // Return the generated password as a string
        return new string(randomPassword.ToArray());
    }
    public async Task GenerateKey()
    {
        byte[] key;
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.GenerateKey();
            key = aes.Key;
        }
        StoreKeyWithCredentialManager(key);
    }
    public void StoreKeyWithCredentialManager(byte[] key)
    {
        string keyAsString = Convert.ToBase64String(key); // Convert to string for storage

        var cm = new Credential
        {
            Target = "NUMP_AES_KEY",
            Password = keyAsString, // Store the key as the password (string format)
            PersistanceType = PersistanceType.LocalComputer // Store on the local machine
        };
        cm.Save();
    }
    public byte[] RetrieveKeyFromCredentialManager()
    {
        var cm = new Credential
        {
            Target = "NUMP_AES_KEY"
        };
        cm.Load();

        if (cm.Password != null)
        {
            return Convert.FromBase64String(cm.Password); // Convert back to byte array
        }
        return null;
    }
    public string EncryptStringToBase64_Aes(string plainText, byte[] key)
    {
        if (plainText == null || plainText.Length <= 0)
            throw new ArgumentNullException(nameof(plainText));
        if (key == null || key.Length <= 0)
            throw new ArgumentNullException(nameof(key));

        byte[] encrypted;
        byte[] iv;

        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.GenerateIV();
            iv = aes.IV;

            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            using (MemoryStream msEncrypt = new MemoryStream())
            {
                using (CryptoStream csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
                {
                    using (StreamWriter swEncrypt = new StreamWriter(csEncrypt))
                    {
                        swEncrypt.Write(plainText);
                    }
                    encrypted = msEncrypt.ToArray();
                }
            }
        }
        // Combine IV and encrypted data
        byte[] result = new byte[iv.Length + encrypted.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(encrypted, 0, result, iv.Length, encrypted.Length);

        // Return as Base64 string
        return Convert.ToBase64String(result);
    }
    public async Task<string> DecryptStringFromBase64_Aes(string cipherTextCombinedBase64, byte[]? key)
    {
        if (key == null)
        {
            key = RetrieveKeyFromCredentialManager();
        }
        
        if (cipherTextCombinedBase64 == null || cipherTextCombinedBase64.Length <= 0)
            throw new ArgumentNullException(nameof(cipherTextCombinedBase64));
        if (key == null || key.Length <= 0)
            throw new ArgumentNullException(nameof(key));

        string? plaintext = null;
        byte[] cipherTextCombined = Convert.FromBase64String(cipherTextCombinedBase64);

        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            int ivLength = aes.BlockSize / 8;
            byte[] iv = new byte[ivLength];
            byte[] cipherText = new byte[cipherTextCombined.Length - ivLength];

            Buffer.BlockCopy(cipherTextCombined, 0, iv, 0, ivLength);
            Buffer.BlockCopy(cipherTextCombined, ivLength, cipherText, 0, cipherText.Length);

            aes.IV = iv;

            ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

            using (MemoryStream msDecrypt = new MemoryStream(cipherText))
            {
                using (CryptoStream csDecrypt = new CryptoStream(msDecrypt, decryptor, CryptoStreamMode.Read))
                {
                    using (StreamReader srDecrypt = new StreamReader(csDecrypt))
                    {
                        plaintext = srDecrypt.ReadToEnd();
                    }
                }
            }
        }

        return plaintext;
    }
}