using System;
using System.Collections.Generic;
using System.Text;
using AESEncryption;


// Currently only the 128bit key size complies with the standard
public enum KeySize
{
    bit128,
    bit192,
    bit256
}

class AESParameters
{
    #region PrivateMembers
    
    private byte[] key;

    #endregion PrivateMembers

    public bool useCBC;
    public bool usePadding;
    public bool useKeyPadding;
    public bool verbose;        
    
    public byte[] InitVector;

    public KeySize KeySize { get; private set;}  //Currently only the 128bit key size complies with the standard

    public byte[] Key 
    {
        get => key; 
        private set { SetKey(value, KeySize);}
    }
    
    public byte[] ExpandedKey { get; private set; }


    public AESParameters(   byte[] _key, 
                            KeySize _size = KeySize.bit128, 
                            bool _cbc = true, 
                            bool _padding = true, 
                            bool _keyPadding = true,
                            byte[] _initVec =  null,
                            bool _verbose = false)
    {
        KeySize = _size;
        Key = _key;
        useCBC = _cbc;
        usePadding = _padding;
        useKeyPadding = _keyPadding;
        verbose = _verbose;

        if(_initVec == null || _initVec.Length < 16)
        {
            _initVec = Encoding.UTF8.GetBytes("1234567890123456");
            if(verbose && useCBC)
                System.Console.WriteLine("Warning: CBC mode on, but initialization vector not supplied or of insuficient length. Using default.");
        }
        
        InitVector = _initVec;

    }

    #region KeyExpansion

    public void SetKey(byte[] _key, KeySize size = KeySize.bit128)
    {
        byte targetSize = 16;
        if (size == KeySize.bit192)
            targetSize = 24;
        else if (size == KeySize.bit256)
            targetSize = 32;

        if(_key.Length < targetSize)
        {
            if(!useKeyPadding) throw new ApplicationException("Error: Key padding set to false and an invalid key lenght has been set.");

            var temp = new byte[targetSize];
            for(int i = 0; i<targetSize; i++)
            {
                if(i < _key.Length)
                    temp[i] = key[i];
                else
                    temp[i] = 0x00;
            }
            _key = temp;
        }

        this.KeySize = size;
        this.key = _key;
        ExpandedKey = KeyExpand(key, size == KeySize.bit128 ? 176 : size == KeySize.bit192 ? 208 : 240, targetSize);
    }

    ///Expands key to needed size
    private byte[] KeyExpand(byte[] _key, int targetSize, byte keySize)
    {

        byte[] expKey = new byte[targetSize];

        //Copy first 16, 24, 32 bytes
        for(int i = 0; i< keySize; i++)
            expKey[i] = _key[i];

        // We generate 4/6/8 bytes at a time and every 16/24/32 generated bytes we increment iteration (for rcon)
        for(int bytesGenerated = keySize, iteration = 1; bytesGenerated < targetSize; bytesGenerated+=(keySize/4))
        {
            byte[] temp = new byte[keySize/4];

            for(int i = 0; i < (keySize/4); i++)
                temp[i] = expKey[bytesGenerated + i - (keySize/4)];

            //Every new iteration we run expansion core
            if(bytesGenerated % keySize == 0)
                temp = KeyExpandCore(temp, iteration++);
            
            //After we have temp values we (DO NOT FORGET TO!) xor with values of same block in previous generation 
            for(int i = 0; i< (keySize/4); i++)
                expKey[bytesGenerated+i] = (byte)(temp[i]^expKey[bytesGenerated + i - keySize]);
            
        }

        if(verbose) System.Console.WriteLine("Expanded key: ");
        if(verbose) System.Console.WriteLine(Program.BytesToFormatedString(expKey));
        return expKey;
    }

    ///Runs core expansion operations for 4 bytes
    private byte[] KeyExpandCore(byte[] _key, int iteration)
    {
        //We shift keys to the left (1byte) and then substitute them using sbox
        byte temp = LookupTables.sBox[_key[0]]; 
        
        for(int i = 0; i < _key.Length; i++)
        {
            if(i+1 == _key.Length)
                _key[i] = temp;
            else
                _key[i] = LookupTables.sBox[_key[i+1]]; 
        }

        _key[0] ^= LookupTables.rcon[iteration]; 

        return _key;
    }

    #endregion KeyExpansion
}

class AESCipher
{
    #region LookupTables

    #endregion
    
    public AESParameters parameters;

    public AESCipher(byte[] key, KeySize size = KeySize.bit128)
    {
        parameters = new AESParameters(key, size);
    }

    public AESCipher(AESParameters _parameters)
    {
        parameters = _parameters;
    }

    private byte[] KeyExpand(byte[] key, int targetSize)
    {

        byte[] expKey = new byte[targetSize];

        for(int i = 0; i< 16; i++)
            expKey[i] = key[i];

        // We generate 4 bytes at a time and every 16 generated bytes we increment iteration (for rcon)
        for(int bytesGenerated = 16, iteration = 1; bytesGenerated < targetSize; bytesGenerated+=4)
        {
            byte[] temp = new byte[4];

            for(int i = 0; i < 4; i++)
                temp[i] = expKey[(bytesGenerated - 4) + i];

            //Every new iteration we run expansion core
            if(bytesGenerated % 16 == 0)
                temp = KeyExpandCore(temp, iteration++);
            
            //After we have temp values we (DO NOT FORGET TO!) xor with values of same block in previous generation 
            expKey[bytesGenerated+0] = (byte)(temp[0] ^ expKey[bytesGenerated+0-16]);
            expKey[bytesGenerated+1] = (byte)(temp[1] ^ expKey[bytesGenerated+1-16]);
            expKey[bytesGenerated+2] = (byte)(temp[2] ^ expKey[bytesGenerated+2-16]);
            expKey[bytesGenerated+3] = (byte)(temp[3] ^ expKey[bytesGenerated+3-16]);
        }

        if(parameters.verbose) System.Console.WriteLine("Expanded key: ");
        if(parameters.verbose) System.Console.WriteLine(Program.BytesToFormatedString(expKey));
        return expKey;
    }

    private byte[] KeyExpandCore(byte[] key, int i)
    {
        //We shift keys to the left (1byte) and then substitute them using sbox
        key = new byte[] {
            LookupTables.sBox[key[1]],
            LookupTables.sBox[key[2]],
            LookupTables.sBox[key[3]],
            LookupTables.sBox[key[0]]
        };

        key[0] ^= LookupTables.rcon[i]; 

        return key;
    }


    #region Encryption

    public byte[] Encrypt(byte[] data)
    {
        List<byte> result = new List<byte>();

        int numberOfRounds = 
            parameters.KeySize == KeySize.bit128 ? 10 :
            parameters.KeySize == KeySize.bit192 ? 12 :
            14;

        for(int bytesEncoded = 0; bytesEncoded <= data.Length; bytesEncoded += 16)
        {
            //Taking a block of data (16 bytes) from plaintext
            #region CreatBlock
            
            byte[] currentFragment = new byte[16];

            if(bytesEncoded+16 <= data.Length)
            {
                for(int i = 0; i < 16; i++)
                    currentFragment[i] = data[bytesEncoded+i];
            }
            else
            {
                if(!parameters.usePadding)
                {
                    if(bytesEncoded == data.Length)
                        break;
                    else
                    {
                        System.Console.WriteLine("Warning: padding is turned off and text contains a non-full block. Cutting off");
                        break;
                    }
                }
                byte remainder = (byte)(16-(data.Length % 16));
                
                if(remainder == 0)
                    remainder = 16;

                for(int i = 0; i < 16; i++ )
                {
                    if(i < 16-remainder)
                        currentFragment[i] = data[bytesEncoded+i];
                    else
                        currentFragment[i] = remainder;
                }

            }
            
            if (parameters.verbose) Console.WriteLine("New block: \t" + Program.BytesToFormatedString(currentFragment));

            #endregion

            //CBC mode XOR'ing with initvec or last encripted block;
            #region CBC

            if(parameters.useCBC)
            {
                byte[] scramble;

                if(bytesEncoded == 0)
                {
                    scramble = parameters.InitVector;
                }
                else
                {
                    scramble = new byte[16];
                    for(int i = 0; i < 16; i++)
                    {
                        scramble[i] = result[bytesEncoded-16+i];
                    }
                }

                for(int i = 0; i < 16; i++)
                {
                    currentFragment[i] ^= scramble[i];
                }

                if(parameters.verbose)
                {
                    System.Console.WriteLine("CBC:");
                    System.Console.WriteLine("Scrambling bytes: \t" + Program.BytesToFormatedString(scramble));
                    System.Console.WriteLine("Scrambled fragment: \t" + Program.BytesToFormatedString(currentFragment));
                }
            }
            #endregion CBC

            //AES encript current block
            #region EncryptBlock

            byte[] subKey = new byte[16];
            
            if (parameters.verbose) Console.WriteLine("Pre round:");
            
            currentFragment = AddRoundKey(currentFragment, parameters.ExpandedKey);

            for(int i = 1; i < numberOfRounds; i++)
            {
                if (parameters.verbose) System.Console.WriteLine("Round {0}:", i);
    
                for(int j = 0; j < 16; j++)
                    subKey[j] = parameters.ExpandedKey[(16*i)+j];

                if (parameters.verbose) System.Console.WriteLine("SubKey: \t" + Program.BytesToFormatedString(subKey));

                currentFragment = SubBytes(currentFragment);
                currentFragment = ShiftRows(currentFragment);  
                currentFragment = MixColumns(currentFragment);
                currentFragment = AddRoundKey(currentFragment, subKey);
                
                if (parameters.verbose) System.Console.WriteLine();
    
            }

            for(int i = 0; i < 16; i++)
                subKey[i] = parameters.ExpandedKey[(parameters.ExpandedKey.Length - 16) + i];

            if (parameters.verbose) System.Console.WriteLine("Round {0}:", numberOfRounds);
            if (parameters.verbose) System.Console.WriteLine("SubKey: \t" + Program.BytesToFormatedString(subKey));

            currentFragment = SubBytes(currentFragment);
            currentFragment = ShiftRows(currentFragment);
            currentFragment = AddRoundKey(currentFragment, subKey);
            
            if (parameters.verbose) System.Console.WriteLine("----------------------------------");


            #endregion
            
            result.AddRange(currentFragment);
        }

        return result.ToArray();
    }

    private byte[] AddRoundKey(byte[] bytes, byte[] roundKey)
    {
        for(int i = 0; i < 16; i++)
            bytes[i] ^= roundKey[i];
        
        if (parameters.verbose) System.Console.WriteLine("After add round key: \t" + Program.BytesToFormatedString(bytes));

        return bytes;
    }

    private byte[] SubBytes(byte[] bytes)
    {
        for(int i = 0; i< 16; i++)
            bytes[i] = LookupTables.sBox[bytes[i]];

        if (parameters.verbose) System.Console.WriteLine("After sub bytes: \t" + Program.BytesToFormatedString(bytes));

        return bytes; 
    }

    private byte[] ShiftRows(byte[] bytes)
    {
        bytes = new byte[]
        {
            bytes[0], bytes[5], bytes[10], bytes[15],
            bytes[4], bytes[9], bytes[14], bytes[3],
            bytes[8], bytes[13],bytes[2], bytes[7],
            bytes[12],bytes[1], bytes[6], bytes[11]
        };

        if (parameters.verbose) System.Console.WriteLine("After shift rows: \t" + Program.BytesToFormatedString(bytes));
        return bytes;
    }
    
    private byte[] MixColumns(byte[] bytes)
    {
        // We use the mul2 & mul3 columns to perform the following matrix (GF)multiplication 
        // 1 - leaves the value; 2 & 3 - performs lshift and xor's (or in GF terms pulinomial multiplication),
        //                               but in this case they are replaced with a table lookup
        // Addition is replaced with XOR
        //  
        //  | 2 3 1 1 |     | byte[0] |
        //  | 1 2 3 1 |  X  | byte[1] |
        //  | 1 1 2 3 |     | byte[2] |
        //  | 3 1 1 2 |     | byte[3] |
        // 
        // We do this with 4 byte matrices (4 times for 16 bytes). Could replace with a for loop and smaller blocks.
        //
        bytes = new byte[]{
            (byte)(LookupTables.mul2[bytes[0]] ^ LookupTables.mul3[bytes[1]] ^ bytes[2] ^ bytes[3]),
            (byte)(bytes[0] ^ LookupTables.mul2[bytes[1]] ^ LookupTables.mul3[bytes[2]] ^ bytes[3]),
            (byte)(bytes[0] ^ bytes[1] ^ LookupTables.mul2[bytes[2]] ^ LookupTables.mul3[bytes[3]]),
            (byte)(LookupTables.mul3[bytes[0]] ^ bytes[1] ^ bytes[2] ^ LookupTables.mul2[bytes[3]]),

            (byte)(LookupTables.mul2[bytes[4]] ^ LookupTables.mul3[bytes[5]] ^ bytes[6] ^ bytes[7]),
            (byte)(bytes[4] ^ LookupTables.mul2[bytes[5]] ^ LookupTables.mul3[bytes[6]] ^ bytes[7]),
            (byte)(bytes[4] ^ bytes[5] ^ LookupTables.mul2[bytes[6]] ^ LookupTables.mul3[bytes[7]]),
            (byte)(LookupTables.mul3[bytes[4]] ^ bytes[5] ^ bytes[6] ^ LookupTables.mul2[bytes[7]]),

            (byte)(LookupTables.mul2[bytes[8]] ^ LookupTables.mul3[bytes[9]] ^ bytes[10] ^ bytes[11]),
            (byte)(bytes[8] ^ LookupTables.mul2[bytes[9]] ^ LookupTables.mul3[bytes[10]] ^ bytes[11]),
            (byte)(bytes[8] ^ bytes[9] ^ LookupTables.mul2[bytes[10]] ^ LookupTables.mul3[bytes[11]]),
            (byte)(LookupTables.mul3[bytes[8]] ^ bytes[9] ^ bytes[10] ^ LookupTables.mul2[bytes[11]]),

            (byte)(LookupTables.mul2[bytes[12]] ^ LookupTables.mul3[bytes[13]] ^ bytes[14] ^ bytes[15]),
            (byte)(bytes[12] ^ LookupTables.mul2[bytes[13]] ^ LookupTables.mul3[bytes[14]] ^ bytes[15]),
            (byte)(bytes[12] ^ bytes[13] ^ LookupTables.mul2[bytes[14]] ^ LookupTables.mul3[bytes[15]]),
            (byte)(LookupTables.mul3[bytes[12]] ^ bytes[13] ^ bytes[14] ^ LookupTables.mul2[bytes[15]])
        };

        if (parameters.verbose) System.Console.WriteLine("After mix colums: \t" + Program.BytesToFormatedString(bytes));

        return bytes;
    }

    #endregion


    #region Decryption

    public byte[] Decrypt(byte[] data)
    {
        List<byte> result = new List<byte>();

        int numberOfRounds = 
            parameters.KeySize == KeySize.bit128 ? 10 :
            parameters.KeySize == KeySize.bit192 ? 12 :
            14;

        for(int bytesDecoded = 0; bytesDecoded < data.Length; bytesDecoded += 16)
        {
            //Taking a block of data (16 bytes) from ciphertext
            #region CreatBlock
            
            byte[] currentFragment = new byte[16];

            if(bytesDecoded+16 <= data.Length)
            {
                for(int i = 0; i < 16; i++)
                    currentFragment[i] = data[bytesDecoded+i];
            }
            else
            {
                Console.WriteLine("Warning: Message is of incorrect length!");
                break;
            }
            #endregion

            #region DecriptBlock

            byte[] subKey = new byte[16];

            for(int i = 0; i < 16; i++)
                subKey[i] = parameters.ExpandedKey[(parameters.ExpandedKey.Length - 16) + i];

            if(parameters.verbose)
            {
                Console.WriteLine("Round: 10:");
                Console.WriteLine("SubKey[ \t \t" + AESEncryption.Program.BytesToFormatedString(subKey)+"];");
                Console.WriteLine("Current fragment: \t" + AESEncryption.Program.BytesToFormatedString(currentFragment));
            }

            currentFragment = AddRoundKey(currentFragment, subKey);
            currentFragment = ReverseShiftRows(currentFragment);
            currentFragment = ReverseSubBytes(currentFragment);
            
            for(int i = numberOfRounds-1; i > 0; i--)
            {
                for(int j = 0; j < 16; j++)
                    subKey[j] = parameters.ExpandedKey[(16*i)+j];

                if(parameters.verbose) Console.WriteLine("Round: " + i);
                if(parameters.verbose) Console.WriteLine("SubKey[ \t \t" + AESEncryption.Program.BytesToFormatedString(subKey)+"];");
                if(parameters.verbose) Console.WriteLine("Current fragment: \t" + AESEncryption.Program.BytesToFormatedString(currentFragment));

                currentFragment = AddRoundKey(currentFragment, subKey);
                currentFragment = ReverseMixColumns(currentFragment);
                currentFragment = ReverseShiftRows(currentFragment);
                currentFragment = ReverseSubBytes(currentFragment);

            }

            if(parameters.verbose) Console.WriteLine();

            currentFragment = AddRoundKey(currentFragment, parameters.ExpandedKey);
            if(parameters.verbose) Console.WriteLine("-----------------------------------");
            
            #endregion

            //CBC mode XOR'ing with initvec or last encripted block;
            #region CBC

            if(parameters.useCBC)
            {
                byte[] scramble;

                if(bytesDecoded == 0)
                {
                    scramble = parameters.InitVector;
                }
                else
                {
                    scramble = new byte[16];
                    for(int i = 0; i < 16; i++)
                    {
                        scramble[i] = data[bytesDecoded-16+i];
                    }
                }

                for(int i = 0; i < 16; i++)
                {
                    currentFragment[i] ^= scramble[i];
                }

                if(parameters.verbose)
                {
                    System.Console.WriteLine("CBC:");
                    System.Console.WriteLine("Scrambling bytes: \t" + Program.BytesToFormatedString(scramble));
                    System.Console.WriteLine("Scrambled fragment: \t" + Program.BytesToFormatedString(currentFragment));
                }
            }

            #endregion CBC

            result.AddRange(currentFragment);
        }

        return result.GetRange(0, result.Count - result[result.Count-1]).ToArray();
    }

    private byte[] ReverseSubBytes(byte[] bytes)
    {
        for(int i = 0; i< 16; i++)
            bytes[i] = LookupTables.rSBox[bytes[i]];
        
        if(parameters.verbose) Console.WriteLine("After r sub bytes: \t" + AESEncryption.Program.BytesToFormatedString(bytes));

        return bytes; 
    }

    private byte[] ReverseMixColumns(byte[] bytes)
    {
        // Same as mixcolumns, but we are solving for the reverse matrix of the GF polynomial
        // We use the following matrix mul for reverse
        //
        // | 14 11 13 09 |     | bytes[0] |
        // | 09 14 11 13 |  X  | bytes[1] |
        // | 13 09 14 11 |     | bytes[2] |
        // | 11 13 09 14 |     | bytes[3] |

        bytes = new byte[] {
            (byte)(LookupTables.mul14[bytes[0]] ^ LookupTables.mul11[bytes[1]] ^ LookupTables.mul13[bytes[2]] ^ LookupTables.mul9[bytes[3]]),
            (byte)(LookupTables.mul9[bytes[0]]  ^ LookupTables.mul14[bytes[1]] ^ LookupTables.mul11[bytes[2]] ^ LookupTables.mul13[bytes[3]]),
            (byte)(LookupTables.mul13[bytes[0]] ^ LookupTables.mul9[bytes[1]]  ^ LookupTables.mul14[bytes[2]] ^ LookupTables.mul11[bytes[3]]),
            (byte)(LookupTables.mul11[bytes[0]] ^ LookupTables.mul13[bytes[1]] ^ LookupTables.mul9[bytes[2]]  ^ LookupTables.mul14[bytes[3]]),
        
            (byte)(LookupTables.mul14[bytes[4]] ^ LookupTables.mul11[bytes[5]] ^ LookupTables.mul13[bytes[6]] ^ LookupTables.mul9[bytes[7]]),
            (byte)(LookupTables.mul9[bytes[4]]  ^ LookupTables.mul14[bytes[5]] ^ LookupTables.mul11[bytes[6]] ^ LookupTables.mul13[bytes[7]]),
            (byte)(LookupTables.mul13[bytes[4]] ^ LookupTables.mul9[bytes[5]]  ^ LookupTables.mul14[bytes[6]] ^ LookupTables.mul11[bytes[7]]),
            (byte)(LookupTables.mul11[bytes[4]] ^ LookupTables.mul13[bytes[5]] ^ LookupTables.mul9[bytes[6]]  ^ LookupTables.mul14[bytes[7]]),

            (byte)(LookupTables.mul14[bytes[8]] ^ LookupTables.mul11[bytes[9]] ^ LookupTables.mul13[bytes[10]] ^ LookupTables.mul9[bytes[11]]),
            (byte)(LookupTables.mul9[bytes[8]]  ^ LookupTables.mul14[bytes[9]] ^ LookupTables.mul11[bytes[10]] ^ LookupTables.mul13[bytes[11]]),
            (byte)(LookupTables.mul13[bytes[8]] ^ LookupTables.mul9[bytes[9]]  ^ LookupTables.mul14[bytes[10]] ^ LookupTables.mul11[bytes[11]]),
            (byte)(LookupTables.mul11[bytes[8]] ^ LookupTables.mul13[bytes[9]] ^ LookupTables.mul9[bytes[10]]  ^ LookupTables.mul14[bytes[11]]),

            (byte)(LookupTables.mul14[bytes[12]] ^ LookupTables.mul11[bytes[13]] ^ LookupTables.mul13[bytes[14]] ^ LookupTables.mul9[bytes[15]]),
            (byte)(LookupTables.mul9[bytes[12]]  ^ LookupTables.mul14[bytes[13]] ^ LookupTables.mul11[bytes[14]] ^ LookupTables.mul13[bytes[15]]),
            (byte)(LookupTables.mul13[bytes[12]] ^ LookupTables.mul9[bytes[13]]  ^ LookupTables.mul14[bytes[14]] ^ LookupTables.mul11[bytes[15]]),
            (byte)(LookupTables.mul11[bytes[12]] ^ LookupTables.mul13[bytes[13]] ^ LookupTables.mul9[bytes[14]]  ^ LookupTables.mul14[bytes[15]])
        };

        if(parameters.verbose) Console.WriteLine("After r mix columns: \t" + AESEncryption.Program.BytesToFormatedString(bytes));

        return bytes;
    }

    private byte[] ReverseShiftRows(byte[] bytes)
    {
        bytes = new byte[] {
            bytes[0], bytes[13],bytes[10],bytes[7],
            bytes[4], bytes[1], bytes[14],bytes[11],
            bytes[8], bytes[5], bytes[2], bytes[15],
            bytes[12],bytes[9], bytes[6], bytes[3]
        };

        if(parameters.verbose) Console.WriteLine("After r shift rows: \t" + AESEncryption.Program.BytesToFormatedString(bytes));

        return bytes;

    }


    #endregion

}