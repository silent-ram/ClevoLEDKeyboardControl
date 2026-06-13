            Method (SCMD, 3, Serialized)
            {
                Name (ARGP, Buffer (0x0100)
                {
                     0x00                                             // .
                })
                Name (ARGS, Zero)
                ARGP = DerefOf (Arg2 [Zero])
                CreateField (ARGP, Zero, 0x20, BINT)
                ARGS = BINT /* \_SB_.DCHU.SCMD.BINT */
                Local0 = Zero
                If ((ToInteger (Arg1) == 0x13))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            ^^PC00.LPCB.EC.ECKS |= 0x80
                        }
                        Else
                        {
                            ^^PC00.LPCB.EC.ECKS &= 0x7F
                        }
                    }

                    Local0 = 0x13
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x14))
                {
                    Local0 = 0x14
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x1D))
                {
                    Local0 = 0x1D
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x1F))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            ^^PC00.LPCB.EC.FDAT = One
                            P80B = 0xDF
                        }
                        Else
                        {
                            ^^PC00.LPCB.EC.FDAT = Zero
                            P80B = 0x5F
                        }

                        ^^PC00.LPCB.EC.FCMD = 0xA4
                    }

                    Local0 = 0x1F
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x20))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            Local1 = 0xC2
                        }
                        Else
                        {
                            Local1 = 0xC3
                        }

                        ^^PC00.LPCB.EC.FDAT = Local1
                        ^^PC00.LPCB.EC.FBUF = 0xA2
                        ^^PC00.LPCB.EC.FCMD = 0xB8
                    }

                    Local0 = 0x20
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x21))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            Local1 = 0xC2
                        }
                        Else
                        {
                            Local1 = 0xC3
                        }

                        ^^PC00.LPCB.EC.FDAT = Local1
                        ^^PC00.LPCB.EC.FBUF = 0xA3
                        ^^PC00.LPCB.EC.FCMD = 0xB8
                    }

                    Local0 = 0x21
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x22))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            Local1 = 0xC2
                        }
                        Else
                        {
                            Local1 = 0xC3
                        }

                        ^^PC00.LPCB.EC.FDAT = Local1
                        ^^PC00.LPCB.EC.FBUF = 0xA1
                        ^^PC00.LPCB.EC.FCMD = 0xB8
                    }

                    Local0 = 0x22
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x26))
                {
                    Local0 = 0x26
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x27))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        ^^PC00.LPCB.EC.FDAT = Zero
                        ^^PC00.LPCB.EC.FBUF = ToInteger (ARGS)
                        ^^PC00.LPCB.EC.FCMD = 0xCA
                    }

                    Local0 = 0x27
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x2A))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            Local1 = 0xC2
                        }
                        Else
                        {
                            Local1 = 0xC3
                        }

                        ^^PC00.LPCB.EC.FDAT = Local1
                        ^^PC00.LPCB.EC.FBUF = 0xA5
                        ^^PC00.LPCB.EC.FCMD = 0xB8
                    }

                    Local0 = 0x2A
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x2C))
                {
                    Local0 = 0x2C
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x31))
                {
                    CreateField (Arg2, Zero, One, KMUT)
                    CreateField (Arg2, One, 0x07, KAUD)
                    If (^^PC00.LPCB.EC.ECOK){}
                    Local0 = 0x31
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x46))
                {
                    HKDR = One
                    PRM0 = 0x12
                    SSMP = 0xC0
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If ((LKFG & One))
                        {
                            ^^PC00.LPCB.EC.FDAT = 0x05
                            ^^PC00.LPCB.EC.FBUF = One
                            ^^PC00.LPCB.EC.FCMD = 0xC4
                        }

                        If ((PSF4 & 0x04))
                        {
                            ^^PC00.LPCB.EC.AIRP |= 0x10
                        }
                    }

                    Return (PSF3) /* \PSF3 */
                }

                If ((ToInteger (Arg1) == 0x47))
                {
                    Local0 = 0x47
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x48))
                {
                    ^^AC.IGNR = One
                    Local0 = 0x48
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x49))
                {
                    Notify (PWRB, 0x80) // Status Change
                    Local0 = 0x49
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x4A))
                {
                    Local0 = 0x4A
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x4C))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            Local1 = 0xC2
                        }
                        Else
                        {
                            Local1 = 0xC3
                        }

                        ^^PC00.LPCB.EC.FDAT = Local1
                        ^^PC00.LPCB.EC.FBUF = 0xA4
                        ^^PC00.LPCB.EC.FCMD = 0xB8
                    }

                    Local0 = 0x4C
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x4E))
                {
                    Local0 = 0x4E
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x4F))
                {
                    Switch (ToInteger (ARGS))
                    {
                        Case (Zero)
                        {
                            P80B = 0x4F
                            If (^^PC00.LPCB.EC.ECOK)
                            {
                                PSF1 &= 0xFFFFFFFFFFFFFFCF
                                ^^AC.ADJP (Zero)
                            }
                        }
                        Case (One)
                        {
                            P80B = 0x5F
                            If (^^PC00.LPCB.EC.ECOK)
                            {
                                PSF1 = ((PSF1 & 0xFFFFFFFFFFFFFFCF) | 0x10)
                                ^^AC.ADJP (Zero)
                            }
                        }
                        Case (0x02)
                        {
                            P80B = 0x6F
                            If (^^PC00.LPCB.EC.ECOK)
                            {
                                PSF1 = ((PSF1 & 0xFFFFFFFFFFFFFFCF) | 0x20)
                                ^^AC.ADJP (Zero)
                            }
                        }

                    }

                    Local7 = ^^PC00.LPCB.EC.CBBL (One)
                    If ((Local7 & 0xFFFF))
                    {
                        If (^^PC00.LPCB.EC.ECOK)
                        {
                            If (^^AC.ACFG)
                            {
                                Local5 = Zero
                                Local6 = One
                            }
                            Else
                            {
                                Local5 = ^^PC00.LPCB.EC.BBST /* \_SB_.PC00.LPCB.EC__.BBST */
                                If ((Local5 == Zero))
                                {
                                    Local5 = (((Local7 >> 0x04) & 0xF0) | (Local7 & 
                                        0x0F))
                                    Local6 = (Local7 & 0x0F)
                                }
                                Else
                                {
                                    Local6 = (Local5 & 0x0F)
                                }
                            }

                            ^^PC00.LPCB.EC.BBST = Local5
                            Notify (^^PC00.PEG1.PEGP, (Local6 | 0xD0))
                        }
                    }

                    Local0 = 0x4F
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x55))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        ^^PC00.LPCB.EC.INF2 |= 0x02
                    }

                    Local0 = 0x55
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x56))
                {
                    Local0 = 0x56
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x57))
                {
                    Local0 = 0x57
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x5A))
                {
                    Switch (ToInteger (ARGS))
                    {
                        Case (Zero)
                        {
                            ^^LID0.WMIF = One
                            Notify (LID0, 0x80) // Status Change
                        }
                        Case (One)
                        {
                            Notify (SLPB, 0x80) // Status Change
                        }
                        Case (0x02)
                        {
                            Notify (PWRB, 0x80) // Status Change
                        }

                    }

                    Local0 = 0x5A
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x5B))
                {
                    PRM1 = ARGS /* \_SB_.DCHU.SCMD.ARGS */
                    PRM0 = 0x07
                    SSMP = 0xC0
                    Local0 = 0x5B
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x5E))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            Local1 = 0xC2
                        }
                        Else
                        {
                            Local1 = 0xC3
                        }

                        ^^PC00.LPCB.EC.FDAT = Local1
                        ^^PC00.LPCB.EC.FBUF = 0xA6
                        ^^PC00.LPCB.EC.FCMD = 0xB8
                    }

                    Local0 = 0x5E
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x65))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS)
                        {
                            Local1 = 0xC2
                        }
                        Else
                        {
                            Local1 = 0xC3
                        }

                        ^^PC00.LPCB.EC.FDAT = Local1
                        ^^PC00.LPCB.EC.FBUF = 0xA9
                        ^^PC00.LPCB.EC.FCMD = 0xB8
                    }

                    Local0 = 0x65
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x66))
                {
                    CAMV = ((ARGS >> 0x10) & 0xFFFF)
                    CAMP = (ARGS & 0xFFFF)
                    PRM0 = 0x0B
                    SSMP = 0xC0
                }

                If ((ToInteger (Arg1) == 0x67))
                {
                    Local2 = ((ARGS >> 0x0C) & 0x0F)
                    If ((Local2 >= 0x0A))
                    {
                        Local2 = Zero
                    }
                    Else
                    {
                        Local2 *= 0x19
                        Local2 = (0xFF - Local2)
                    }

                    Local3 = ((ARGS >> 0x10) & 0xFF)
                    Local4 = ((ARGS >> 0x18) & 0x0F)
                    Local7 = ((ARGS >> 0x1C) & 0x0F)
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (((Local7 >= 0x07) && (Local7 <= 0x0B)))
                        {
                            ^^PC00.LPCB.EC.FDAT = Local7
                            ^^PC00.LPCB.EC.FCMD = 0xC4
                        }
                        Else
                        {
                            If ((Local7 == Zero))
                            {
                                Local0 = Zero
                                Local0 = (ARGS & 0x07)
                                Local0 |= ((ARGS >> One) & 0x38)
                                Local0 |= ((ARGS >> 0x02) & 0x01C0)
                                ^^PC00.LPCB.EC.FDAT = Local0
                                ^^PC00.LPCB.EC.FBUF = (Local0 >> 0x08)
                                ^^PC00.LPCB.EC.FCMD = 0xC2
                            }

                            If ((Local7 == One))
                            {
                                ^^PC00.LPCB.EC.FDAT = 0x03
                                ^^PC00.LPCB.EC.FBUF = Local3
                                ^^PC00.LPCB.EC.FCMD = 0xC4
                            }

                            If ((Local7 == 0x02))
                            {
                                ^^PC00.LPCB.EC.FDAT = 0x04
                                ^^PC00.LPCB.EC.FBUF = Local3
                                ^^PC00.LPCB.EC.FCMD = 0xC4
                            }

                            If ((Local7 == 0x03))
                            {
                                ^^PC00.LPCB.EC.FDAT = 0x06
                                ^^PC00.LPCB.EC.FBUF = Local3
                                ^^PC00.LPCB.EC.FBF1 = Local4
                                ^^PC00.LPCB.EC.FCMD = 0xC4
                            }

                            If ((Local7 == 0x04))
                            {
                                If (Local3)
                                {
                                    Local0 = 0x0D
                                }
                                Else
                                {
                                    Local0 = 0x0E
                                }

                                ^^PC00.LPCB.EC.FDAT = Local0
                                ^^PC00.LPCB.EC.FCMD = 0xC4
                            }

                            If ((Local7 == 0x0C)){}
                            If ((Local7 == 0x0D))
                            {
                                ^^PC00.LPCB.EC.FDAT = 0x02
                                ^^PC00.LPCB.EC.FBUF = Local2
                                ^^PC00.LPCB.EC.FCMD = 0xC4
                            }

                            If ((Local7 == 0x0E))
                            {
                                Local1 = ((ARGS >> 0x0E) & 0x1F)
                                If ((ARGS & 0x2000))
                                {
                                    Local1 |= 0x20
                                }

                                ^^PC00.LPCB.EC.FDAT = 0x0C
                                ^^PC00.LPCB.EC.FBUF = Local1
                                ^^PC00.LPCB.EC.FCMD = 0xC4
                            }

                            If ((Local7 == 0x0F))
                            {
                                Local6 = Zero
                                Local3 = (ARGS & 0xFF)
                                Local2 = ((ARGS >> 0x08) & 0xFF)
                                Local1 = ((ARGS >> 0x10) & 0xFF)
                                If ((Local4 < 0x03))
                                {
                                    Local0 = (Local4 + 0x03)
                                    Local6 = 0xCA
                                }
                                ElseIf ((Local4 == 0x03))
                                {
                                    Local0 = 0x07
                                    Local6 = 0xCA
                                }
                                ElseIf ((Local4 == 0x04))
                                {
                                    Local0 = 0x06
                                    Local1 = (ARGS & 0xFF)
                                    Local6 = 0xCA
                                }
                                ElseIf ((Local4 == 0x06))
                                {
                                    ^^PC00.LPCB.EC.FDAT = 0x09
                                    ^^PC00.LPCB.EC.FBUF = Local1
                                    ^^PC00.LPCB.EC.FBF1 = Local2
                                    ^^PC00.LPCB.EC.FBF2 = Local3
                                    ^^PC00.LPCB.EC.FCMD = 0xCA
                                    Local0 = 0x0A
                                    Local6 = 0xCA
                                }

                                If (Local6)
                                {
                                    ^^PC00.LPCB.EC.FDAT = Local0
                                    ^^PC00.LPCB.EC.FBUF = Local1
                                    ^^PC00.LPCB.EC.FBF1 = Local2
                                    ^^PC00.LPCB.EC.FBF2 = Local3
                                    ^^PC00.LPCB.EC.FCMD = Local6
                                }
                            }
                        }
                    }

                    Local0 = 0x67
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x68))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        Local4 = ARGS /* \_SB_.DCHU.SCMD.ARGS */
                        ^^PC00.LPCB.EC.FDAT = One
                        ^^PC00.LPCB.EC.FBUF = (Local4 & 0xFF)
                        ^^PC00.LPCB.EC.FCMD = 0xC1
                        ^^PC00.LPCB.EC.FDAT = 0x02
                        ^^PC00.LPCB.EC.FBUF = ((Local4 >> 0x08) & 0xFF)
                        ^^PC00.LPCB.EC.FCMD = 0xC1
                        ^^PC00.LPCB.EC.FDAT = 0x03
                        ^^PC00.LPCB.EC.FBUF = ((Local4 >> 0x10) & 0xFF)
                        ^^PC00.LPCB.EC.FCMD = 0xC1
                        ^^PC00.LPCB.EC.FDAT = 0x04
                        ^^PC00.LPCB.EC.FBUF = ((Local4 >> 0x18) & 0xFF)
                        ^^PC00.LPCB.EC.FCMD = 0xC1
                    }

                    Local0 = 0x68
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x69))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        Local4 = ARGS /* \_SB_.DCHU.SCMD.ARGS */
                        If ((Local4 & One))
                        {
                            ^^PC00.LPCB.EC.FDAT = 0xFF
                            ^^PC00.LPCB.EC.FBUF = One
                            ^^PC00.LPCB.EC.FCMD = 0xC1
                        }

                        If ((Local4 & 0x02))
                        {
                            ^^PC00.LPCB.EC.FDAT = 0xFF
                            ^^PC00.LPCB.EC.FBUF = 0x02
                            ^^PC00.LPCB.EC.FCMD = 0xC1
                        }

                        If ((Local4 & 0x04))
                        {
                            ^^PC00.LPCB.EC.FDAT = 0xFF
                            ^^PC00.LPCB.EC.FBUF = 0x03
                            ^^PC00.LPCB.EC.FCMD = 0xC1
                        }

                        If ((Local4 & 0x08))
                        {
                            ^^PC00.LPCB.EC.FDAT = 0xFF
                            ^^PC00.LPCB.EC.FBUF = 0x04
                            ^^PC00.LPCB.EC.FCMD = 0xC1
                        }
                    }

                    Local0 = 0x69
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x6A))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        Local0 = ((ARGS >> 0x08) & 0xFF)
                        Local1 = (ARGS & 0xFF)
                        ^^PC00.LPCB.EC.FDAT = Local0
                        ^^PC00.LPCB.EC.FBUF = Local1
                        ^^PC00.LPCB.EC.FCMD = 0xBA
                    }

                    Local0 = 0x6A
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x6B))
                {
                    INDX = ARGS /* \_SB_.DCHU.SCMD.ARGS */
                    Local0 = 0x6B
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x6C))
                {
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ARGS){}
                        Else
                        {
                        }
                    }

                    Local0 = 0x6C
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x6D))
                {
                    If (((OSYS >= 0x07DD) && (HEFE == One)))
                    {
                        ^^HIDD.HPEM (0x08)
                    }

                    Local0 = 0x6D
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x74))
                {
                    CreateField (Arg2, Zero, One, SMUT)
                    CreateField (Arg2, One, 0x07, SAUD)
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        If (ToInteger (SMUT))
                        {
                            Local0 = Zero
                        }
                        Else
                        {
                            Local0 = ToInteger (SAUD)
                            If ((Local0 > 0x64))
                            {
                                Local0 = 0x64
                            }
                        }

                        If (((LKFG & 0x0C) == 0x04)){}
                        Else
                        {
                            ^^PC00.LPCB.EC.FDAT = Local0
                            ^^PC00.LPCB.EC.FCMD = 0xC6
                        }
                    }

                    Local0 = 0x74
                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x75))
                {
                    Local1 = (INDX & 0xFFFF)
                    Local2 = ((INDX >> 0x10) & 0xFFFF)
                    If ((Local2 > 0x02))
                    {
                        Local2 = Zero
                    }

                    If ((Local1 > 0xFF))
                    {
                        Return (Ones)
                    }

                    Local2 = (One << Local2)
                    Local3 = Local2
                    Local3--
                    If (((Local1 + Local3) > 0xFF))
                    {
                        Return (Ones)
                    }

                    Local0 = (Local1 + 0xFE0B0100)
                    If (^^PC00.LPCB.EC.ECOK)
                    {
                        Name (WBUF, Buffer (0x04)
                        {
                             0x00, 0x00, 0x00, 0x00                           // ....
                        })
                        CreateByteField (WBUF, Zero, ARGB)
                        CreateWordField (WBUF, Zero, ARGW)
                        CreateDWordField (WBUF, Zero, ARGD)
                        WBUF = ARGS /* \_SB_.DCHU.SCMD.ARGS */
                        If (((Local1 <= 0xD9) && ((Local1 + Local3) >= 
                            0xD9)))
                        {
                            Local4 = (0xD9 - Local1)
                            Local6 = (DerefOf (WBUF [Local4]) & 0x40)
                            Local7 = (^^PC00.LPCB.EC.AIRP & 0xBF)
                            WBUF [Local4] = (Local6 | Local7)
                        }

                        OperationRegion (WH2M, SystemMemory, Local0, Local2)
                        Switch (ToInteger (Local2))
                        {
                            Case (One)
                            {
                                Field (WH2M, ByteAcc, Lock, Preserve)
                                {
                                    WHMB,   8
                                }

                                WHMB = ARGB /* \_SB_.DCHU.SCMD.ARGB */
                            }
                            Case (0x02)
                            {
                                Field (WH2M, ByteAcc, Lock, Preserve)
                                {
                                    WHMW,   16
                                }

                                WHMW = ARGW /* \_SB_.DCHU.SCMD.ARGW */
                            }
                            Case (0x04)
                            {
                                Field (WH2M, ByteAcc, Lock, Preserve)
                                {
                                    WHMD,   32
                                }

                                WHMD = ARGD /* \_SB_.DCHU.SCMD.ARGD */
                            }

                        }

                        If (((Local1 <= 0xF8) && ((Local1 + Local3) >= 
                            0xF8)))
                        {
                            Local4 = (0xF8 - Local1)
                            ^^PC00.LPCB.EC.FCMD = DerefOf (WBUF [Local4])
                        }

                        Local0 = 0x75
                    }
                    Else
                    {
                        Local0 = Ones
                    }

                    Return (Local0)
                }

                If ((ToInteger (Arg1) == 0x76))
                {
                    Local1 = ((ARGS & 0xFF000000) >> 0x18)
                    Local2 = (ARGS & 0x00FFFFFF)
                    Switch (ToInteger (Local1))
                    {
                        Case (One)
                        {
                            ToBCD ((Local2 & 0x3F), C602) /* \_SB_.DCHU.C602 */
                            ToBCD (((Local2 >> 0x06) & 0x3F), C603) /* \_SB_.DCHU.C603 */
                            ToBCD (((Local2 >> 0x0C) & 0x1F), C604) /* \_SB_.DCHU.C604 */
                            ToBCD (((Local2 >> 0x11) & 0x07), C601) /* \_SB_.DCHU.C601 */
                            If (^^PC00.LPCB.EC.ECOK)
                            {
                                ^^PC00.LPCB.EC.FBUF = C602 /* \_SB_.DCHU.C602 */
                                ^^PC00.LPCB.EC.FBF1 = C603 /* \_SB_.DCHU.C603 */
                                ^^PC00.LPCB.EC.FBF2 = C604 /* \_SB_.DCHU.C604 */
                                ^^PC00.LPCB.EC.FDAT = C601 /* \_SB_.DCHU.C601 */
                                ^^PC00.LPCB.EC.FCMD = 0xC6
                            }
                        }
                        Case (0x02)
