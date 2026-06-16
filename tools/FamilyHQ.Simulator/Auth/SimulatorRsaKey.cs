using System.Security.Cryptography;

namespace FamilyHQ.Simulator.Auth;

public static class SimulatorRsaKey
{
    public static readonly RSA Instance = RSA.Create(2048);
}
