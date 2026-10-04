using System.Text.RegularExpressions;
using LcdMod.Common.Imaging;
using VRageMath;

namespace Arthur_s_Lcd_Mod.Gui.Tests;

public sealed class SkyboxCubemapTests
{
    // Deterministic valid blocks, covering every mode and random partition/anchor/
    // rotation/index-selection values. Expected RGBA decoded independently with Pillow 12.3.
    public static IEnumerable<object[]> Bc7Vectors()
    {
        yield return new object[] { "f332e029cdd79577f307dfbdb6e64cc8", "817fbfff8f6ab7ff8b71baff9463b5ff0edbaeff0dd5aaff0dd5aaff0ac9a3ff0fe1b1ff09c3a0ff0fe1b1ff0dd5aaffccdcc7ffffefffffe6e6e3ffb3d3abff" };
        yield return new object[] { "15ccf33f5045e722c65158e2f082201e", "2d444dff1e3359ff1e3359ff3e583effc46d1effe7a510ffc46d1effc46d1efff795d9fff7a5e7fff795d9fff7a5e7fff795d9fff74192fff774bdfff7a5e7ff" };
        yield return new object[] { "cfb69ce957034bacf1244d7d54c27b28", "75f531ff80ea39ffc340a1ffef188cff75f531ff80ea39ffc340a1ffad53abffc68473ff7e6c48ffef188cffd92c96ff927254ffc68473ffc340a1ff52a5d6ff" };
        yield return new object[] { "5f0f8f9baa389b70ddf2f595742248b8", "9f4f5dff7b5a8cff80ade1ff877bcbffd4bf84ff905370ff825883ff8a6ac4ffc99e6bffd4bf84ffad4a4aff905370ffd4bf84ffc69463ffdbd494ff895679ff" };
        yield return new object[] { "0ac861a06f044383b81e20d4abfe8971", "20bd0cff3f9d84ff18c1adff7a6445ff1d5577ff528b70ff7a6445ff3f9d84ff1c4489ffa1401cff528b70ff3f9d84ff1e7754ff2baf99ff8e5230ff2baf99ff" };
        yield return new object[] { "860a711163573997eb4ad0054dbb1d66", "288d5cff177b9fff1e8283ff258969ff5c54b9ff254068ff5150a9ff254068ff177b9fff1e8283ff1e8283ff1074b9ff5c54b9ff1b3c58ff304478ff5150a9ff" };
        yield return new object[] { "5ee5d8996a201462721e08ef860c1ef0", "937b6dff74089dff94122eff840d67ff8d0424ff8e1b32ff74089dff7e0b79ff8e1b32ff95a989ff890f52ff99141cff95a989ff95a989ff8e1b32ff840d67ff" };
        yield return new object[] { "8a1d20c19af3056c68a41b25c181997b", "555da7ff5b6d2eff555da7ff5b6d2eff5b6d2eff6663adff4a7e1aff12418dff6663adff4a7e1aff12418dff90396cff5b6d2eff023a87ffb21793ff4556a0ff" };
        yield return new object[] { "f4284b610dfbf748c2179b75e6960fa9", "a5b5f7ff79e76dff0818ceff1e59c6ff8fcdb3ff349dbdff2e23d1ff1810ceff63ff29ff4adeb5ff5a4ad6ff2e23d1ffa5b5f7ff8fcdb3ff349dbdff349dbdff" };
        yield return new object[] { "bca35ff3ce5085aad459603dcd08d1fd", "af3e89ff8c0873fff7adb5ff8c0873ffd4779fff8c0873ff8c0873ffaf3e89ffbdadf7ff70263bff70263bff9ca5a5ffa7a8c0ff9c52b5ff9c52b5ffb2aadcff" };
        yield return new object[] { "d49d356350ca14e0f970dcbe0e18b139", "898f72ff73a539ff18e7ffff18e7ffff201b89ff2937a8ff18e7ffff46dff2ff3152c6ff201b89ff46dff2ffa5ced6ff73a539ffb563e7ff46dff2ff18e7ffff" };
        yield return new object[] { "fcb9caef931931a6e0f801c78fac959e", "b69777ffcec618ff96c661ffcec618ff6a36b3ffb69777ffb3c63bff7bc684ffffa518ffb66f64ffb69777ff96c661ff6a36b3ff2100ffffb66f64ff8391b9ff" };
        yield return new object[] { "38ecf223e95af3bd3e5ee4f499ee6eee", "77d71ffff3355fff77d71fff66b9ccfff3355fffa0a234ffa4aed2ff66b9ccfff3355fffa0a234ffa4aed2ff47bfc9fff3355fff66b9ccffa4aed2ff66b9ccff" };
        yield return new object[] { "98439031ac1d568c6d151b65478cd9fb", "45bf82ff638b37ff977076ff21edb7ff6c8e49ff45bf82ff638b37ffb16395ff21edb7ff906014ff977076ffb16395ff7d7e56ff906014ff906014ff7d7e56ff" };
        yield return new object[] { "b875ee542f728c2a8087bd8edcb32513", "75a2adffefc787ffa8507affbc0a3affefc787ffa8507affbc0a3affb4b59aff75a2adffaf3965ffb5214fff3a90c0ffbc0a3affa8507aff75a2adff3a90c0ff" };
        yield return new object[] { "18e945afce0447d478fe6a5843997cd7", "f5273dff5f89d5ff5f89d5ff536dafffbb3f7cff7e58bfffbb3f7cff475187fff5273dff4470feff4470feffbb3f7cff4470feffbb3f7cffbb3f7cff4470feff" };
        yield return new object[] { "d05e02798eadb8569b79b1d088d86f4e", "f7db3900a228397fdba0392af7a03900b028396ae9633915db63392abe633956f7a03900cd28393e94db399494283994a2db397fbe283956cd28393edb63392a" };
        yield return new object[] { "307b243e7c36e503ba5d34c620d4559b", "5e7e619d504a18de65e7f7185be7f718577e619d624a18de654a18de624a18de577e619d5ee7f7184d7e619d5ee7f71854b3ae5950e7f71850b3ae59574a18de" };
        yield return new object[] { "d042a8eb7a86d2f5d4ab9761488198de", "1065a77f108e9670108edeae10a27352108edeae108e735210a2967010799670108e8461108e7352108e967010a2bb90107984611079cc9f1079efbd10a2deae" };
        yield return new object[] { "500ce5f29ecdec844a02ef4107ceb225", "58937b9858607b9842307b2958db7b984d787b5f63487bce63c37bce58db7b9858487b9858c37b984d937b5f63c37bce58937b9863937bce63c37bce4dc37b5f" };
        yield return new object[] { "e06f250374b6b97cfae086c9bb7d7a4e", "df1853cf95407a6c95409f6cc7257aafdf1853cfdf189fcf95409f6cc72553af95407a6cdf187acfdf189fcf9540536cdf187acfc7259fafad332e8c9540536c" };
        yield return new object[] { "602db6d63c622585537a4f3d0f7c5be9", "7bb5465ae1c552af49c552af49b5465a49bd4c84e1cd58d9e1cd58d97bc552afe1cd58d9afbd4c847bc552af7bc552af7bc552afafcd58d9afbd4c84e1c552af" };
        yield return new object[] { "a067e34a5e308112b72d2416f2ebff5a", "b9ac2085a3a036b6b9c420858dc44ce5a3c436b6b9b82085b9b82085cfc40a56a3c436b6cfc40a56b9c42085cfc40a568db84ce5a3b836b6cfac0a56cfac0a56" };
        yield return new object[] { "a0b865a1bc8764e75e9a90484898dbf5", "7dd9b04997c420cb8ad9678c70cff70a7dd9b04997c420cb70cff70a7dc4b04970b9f70a8ac4678c70cff70a7db9b04970cff70a7dcfb0498ab9678c70b9f70a" };
        yield return new object[] { "c01165099cd68c0db6ec65067d99dc9b", "40929a7530867a382e8476312a816e213c8f92673a8d8e5e3a8d8e5e4696a68c2c83722a388c8a5734898248348982482e8476312c83722a30867a3834898248" };
        yield return new object[] { "c06e4683ba558a6d99f992d27477f33a", "963c5ca06a4546ba6a4546ba33512bdba73865956a4546baa7386595464d34d0963c5ca07b424fb07b424fb07b424fb09f3a609a33512bdb5f4741c09f3a609a" };
        yield return new object[] { "40ae5ad6dfbcd289a37189be3069198f", "bb6e3ac7ccca5252bb6e3ac7c6ab4a79cabe4f61c8b54c6dd3f25d1fced35446b96537d3bf833faccabe4f61c4a24785cabe4f61bb6e3ac7d5fb5f13c8b54c6d" };
        yield return new object[] { "402a4a4bbd57b652c1ca57fc56cb8d64", "a8b4eeb663ab53a86ead6bab63ab53a87faf93ae8bb0aeb063ab53a851a92ba585b09faf8bb0aeb068ac5faa63ab53a85dab46a77aae86ad91b1bab185b09faf" };
        yield return new object[] { "80e11aedf601687f8f2cb238bd0b8d32", "44a6c6461818792044a6c6461818792086385e8986385e896d047d5da16e3fb72d5d9e3344a6c64659ebeb5944a6c64686385e89a16e3fb786385e896d047d5d" };
        yield return new object[] { "807166c592615130254430fcb56ab0e5", "ac211961881f273158902063881f2731881f2731866714ac866714ac58902063cf240c8ecf240c8eb24108f3881f2731ac211961ac211961881f2731651c3404" };
        yield return new object[] { "802d08cb774a692bf6f75c1b05f4f3d6", "03d354e100eb69eb5da6b6ae5da6b6aec2a3e480c2a3e48008a228cb08a228cb8ea5cd97c2a3e48008a228cb03d354e108a228cb05ba3dd6c2a3e4808ea5cd97" };
        yield return new object[] { "80cc2a95ff6ac9cd61b3ee29cc2994de", "49e0c08e49e0c08e38c5c5bee320dba259fbba61bd824dd8bd824dd8aab208f3d05096bdd05096bdaab208f3bd824dd8e320dba2e320dba2d05096bdbd824dd8" };
    }

    [Theory]
    [MemberData(nameof(Bc7Vectors))]
    public void AllBc7ModesMatchIndependentDecoder(string encoded, string expected)
    {
        var output = new byte[64];
        new Bc7Decoder().Decode(Convert.FromHexString(encoded), 0, output);
        Assert.Equal(Convert.FromHexString(expected), output);
    }

    [Fact]
    public void DdsFacesUseDirect3dOrderAndUvDirections()
    {
        using var stream = MakeCube();
        var cube = SkyboxCubemap.Load(stream);
        for (int face = 0; face < 6; face++)
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            Assert.Equal(new Color(face * 30, x * 100, y * 100),
                cube.Sample(FaceRay(face, x - .5f, y - .5f)));
    }

    [Fact]
    public void MissingFacesAndTruncatedMipAreRejected()
    {
        using var valid = MakeCube();
        byte[] data = valid.ToArray();
        data[112] = data[113] = 0;
        Assert.Throws<InvalidOperationException>(() => SkyboxCubemap.Load(new MemoryStream(data)));
        Assert.Throws<InvalidOperationException>(() => SkyboxCubemap.Load(new MemoryStream(valid.ToArray()[..^1])));
    }

    [Fact]
    public void BilinearSamplingAndMiplessDownsamplingStayInsideTheCube()
    {
        using var stream = MakeCube();
        var cube = SkyboxCubemap.Load(stream);
        Assert.Equal(new Color(0, 50, 50), cube.Sample(Vector3.Right));
        stream.Position = 0;
        var small = SkyboxCubemap.Load(stream, 1);
        Assert.Equal(new Color(0, 0, 0), small.Sample(Vector3.Right));
    }

    [Fact]
    public void StockGameSkyboxDecodesAllSixFaces()
    {
        var repo = new DirectoryInfo(Path.GetDirectoryName(SourceFile())!);
        while (repo != null && !File.Exists(Path.Combine(repo.FullName, "LcdMod.sln"))) repo = repo.Parent;
        string ini = File.ReadAllText(Path.Combine(repo!.FullName, "Arthur-s-Lcd-Mod/mdk.local.ini"));
        string bin = Regex.Match(ini, @"(?m)^binarypath=(.+)").Groups[1].Value.Trim();
        using var file = File.OpenRead(Path.Combine(bin, "../Content/Textures/BackgroundCube/Final/BackgroundCube.dds"));
        var cube = SkyboxCubemap.Load(file);
        for (int face = 0; face < 6; face++)
        {
            var colors = new HashSet<uint>();
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                Color color = cube.Sample(FaceRay(face, x / 16f - 1f, y / 16f - 1f));
                Assert.Equal(255, color.A);
                colors.Add(color.PackedValue);
            }
            Assert.True(colors.Count > 16, "A skybox face should retain its stars and nebulae.");
        }
    }

    static Vector3 FaceRay(int face, float u, float v) => face switch
    {
        0 => new Vector3(1, -v, -u),
        1 => new Vector3(-1, -v, u),
        2 => new Vector3(u, 1, v),
        3 => new Vector3(u, -1, -v),
        4 => new Vector3(u, -v, 1),
        _ => new Vector3(-u, -v, -1)
    };

    static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    static MemoryStream MakeCube()
    {
        var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
        byte[] header = new byte[128];
        void Set(int offset, uint value) => BitConverter.GetBytes(value).CopyTo(header, offset);
        Set(0, 0x20534444); Set(4, 124); Set(12, 2); Set(16, 2); Set(28, 1);
        Set(76, 32); Set(80, 0x40); Set(88, 32); Set(92, 255); Set(96, 0xff00); Set(100, 0xff0000);
        Set(112, 0xfe00);
        writer.Write(header);
        for (int face = 0; face < 6; face++)
        for (int y = 0; y < 2; y++)
        for (int x = 0; x < 2; x++)
            writer.Write(new byte[] { (byte)(face * 30), (byte)(x * 100), (byte)(y * 100), 255 });
        stream.Position = 0;
        return stream;
    }
}
