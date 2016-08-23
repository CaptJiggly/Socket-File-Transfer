using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Serialization.Formatters.Binary;
public class PacketWriter : BinaryWriter
{
    private MemoryStream _ms;
    private BinaryFormatter _bf;

    public PacketWriter()
        : base()
    {
        _ms = new MemoryStream();
        _bf = new BinaryFormatter();
        OutStream = _ms;
    }

    public void Write(Image image)
    {
        var ms = new MemoryStream();

        image.Save(ms, ImageFormat.Png);

        ms.Close();

        byte[] imageBytes = ms.ToArray();

        Write(imageBytes.Length);
        Write(imageBytes);
    }

    public void WriteT(object obj)
    {
        _bf.Serialize(_ms, obj);
    }

    public byte[] GetBytes()
    {
        Close();

        byte[] data = _ms.ToArray();

        return data;
    }
}

public class PacketReader : BinaryReader
{
    private BinaryFormatter _bf;
    public PacketReader(byte[] data)
        : base(new MemoryStream(data))
    {
        _bf = new BinaryFormatter();
    }

    public Image ReadImage()
    {
        int len = ReadInt32();

        byte[] bytes = ReadBytes(len);

        Image img;

        using (MemoryStream ms = new MemoryStream(bytes))
        {
            img = Image.FromStream(ms);
        }

        return img;
    }

    public T ReadObject<T>()
    {
        return (T)_bf.Deserialize(BaseStream);
    }
}