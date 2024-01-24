using System.ComponentModel.DataAnnotations;

namespace DarkDPD;

class Program
{
    static double[] LUT = new double[2048];
    static void Main(string[] args)
    {
        GenerateLUT();
        PlotFunctions();

        double[] pureSamples = new double[10 * 48000];
        double[] pureSamplesQuiet = new double[10 * 48000];


        //Generate 10 seconds of 48Khz sample rate audio, single 700hz tone.
        double phase = 0;
        for (int i = 0; i < pureSamples.Length; i++)
        {
            pureSamples[i] = Math.Sin(phase);
            pureSamplesQuiet[i] = 0.1 * Math.Sin(phase);
            phase += 700.0 * 2.0 * Math.PI / 48000.0;

            //This helps very small errors with double precision - ignore.
            phase %= 2.0 * Math.PI;
            //phase2 %= 2.0 * Math.PI;
        }

        SaveAudio(pureSamples, "pure.raw");
        SaveAudio(pureSamplesQuiet, "purequiet.raw");
        ModulateDSB(pureSamples, "pure-rf.raw");
        ModulateDSB384(pureSamples, "pure-rf384.raw");

        //Run through a linear amplifier. This is voltage, so double voltage is 4x power, 6db increase.
        double[] amplified = new double[pureSamplesQuiet.Length];
        for (int i = 0; i < amplified.Length; i++)
        {
            //This is a linear transfer function, 6db gain.
            amplified[i] = LinearAmplifier(pureSamplesQuiet[i]);
        }

        SaveAudio(amplified, "amplified.raw");
        ModulateDSB(amplified, "amplified-rf.raw");

        //Same as above, but with added IMD.
        double[] distorted = new double[pureSamples.Length];
        for (int i = 0; i < distorted.Length; i++)
        {
            //This is a transfer function with IMD3, "droop at the end".
            distorted[i] = OverdrivenAmplifier(pureSamples[i]);
        }

        SaveAudio(distorted, "distorted.raw");
        ModulateDSB(distorted, "distorted-rf.raw");
        ModulateDSB384(distorted, "distorted-rf384.raw");

        //If we know the transfer function of the amplifier, we can "predistort" it.
        //Real predistortion will cancel out IMD3, IMD5, IMD7 etc.

        //This first predistorts the audio and then goes through the amplifier, redistorting it.
        double[] undistorted = new double[distorted.Length];
        for (int i = 0; i < undistorted.Length; i++)
        {
            //In = out + 0.2 * out^3.
            undistorted[i] = OverdrivenAmplifier(Predistort(pureSamples[i]));
        }

        SaveAudio(undistorted, "undistorted.raw");
        ModulateDSB(undistorted, "undistorted-rf.raw");
        ModulateDSB384(undistorted, "undistorted-rf384.raw");

        double[] voice = ReadAudio("voice.raw");
        double[] voiceDistort = new double[voice.Length];
        double[] voiceUndistort = new double[voice.Length];
        for (int i = 0; i < voice.Length; i++)
        {
            voiceDistort[i] = OverdrivenAmplifier(voice[i]);
            voiceUndistort[i] = voice[i];
        }
        SaveAudio(voiceDistort, "voicedistort.raw");
        ModulateDSB384(voiceDistort, "voicedistort384.raw");
        ModulateDSB384(voice, "rfnodistort.raw");
        ModulateDSB384Distort(voice, "rfdistort.raw");
        ModulateDSB384Distort(voiceUndistort, "rfundistort.raw");
        ModulateDSB384Undistort(voice, "rfstageundistort.raw");
    }

    //We need the inverse of the transfer function.
    //Using a LUT we can ignore the horrible math of finding the inverse of x + x^3 + ...
    static void GenerateLUT()
    {
        //0 input will always be 0 output, skip it.
        for (int i = 1; i < LUT.Length; i++)
        {
            double percentage = i / (double)(LUT.Length - 1);
            double min = 0;
            double max = 1;
            //We can only drive up to the maximum output of the amplifier, so this will become our new 100%.
            //We need to target a linear transfer curve mapping 0-100% -> 0-80%.
            double target = OverdrivenAmplifier(1.0) * percentage;
            double error = 1.0;
            double halfway = 0;
            //Lets get errors down to a certain theshold by guessing.
            //This is -80db.
            while (error > 0.0001)
            {
                halfway = (min + max) / 2.0;
                double current = OverdrivenAmplifier(halfway);
                if (current < target)
                {
                    min = halfway;
                }
                else
                {
                    max = halfway;
                }
                error = Math.Abs(target - current);
            }
            LUT[i] = halfway;
        }
    }

    //These are our transfer functions.
    //6db gain (2x voltage = 4x power, 6db)
    static double LinearAmplifier(double input)
    {
        return 2.0 * input;
    }

    //An amplifier that only reaches 80% output given 100% input.
    static double OverdrivenAmplifier(double input)
    {
        return input - 0.2 * Math.Pow(input, 3);
    }

    //Look up the value in the precomputed table. Saves us a ton of ugly math.
    static double Predistort(double input)
    {
        bool negative = false;
        if (input < 0)
        {
            negative = true;
            input = -input;
        }
        //Requesting max output, cannot interpolate
        if (input > LUT[LUT.Length - 1])
        {
            if (negative)
            {
                return -LUT[LUT.Length - 1];
            }
            return LUT[LUT.Length - 1];
        }
        //Interpolate the output
        int index = (int)(input * (LUT.Length - 1));
        double leftOver = input - (index / (double)LUT.Length);
        double output = ((1.0 - leftOver) * LUT[index]) + (leftOver * LUT[index + 1]);
        if (negative)
        {
            output = -output;
        }
        return output;
    }

    static void SaveAudio(double[] samples, string filename)
    {
        File.Delete(filename);
        using (FileStream fs = new FileStream(filename, FileMode.Create))
        {
            for (int i = 0; i < samples.Length; i++)
            {
                short s16 = (short)(samples[i] * short.MaxValue);
                byte lsb = (byte)(s16 & 0xFF);
                byte msb = (byte)(s16 >> 8);
                fs.WriteByte(lsb);
                fs.WriteByte(msb);
            }
        }
    }

    static double[] ReadAudio(string filename)
    {
        double[] samples;
        using (FileStream fs = new FileStream(filename, FileMode.Open))
        {
            samples = new double[fs.Length / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                byte lsb = (byte)fs.ReadByte();
                byte msb = (byte)fs.ReadByte();
                short s16 = (short)(msb << 8 | lsb);
                samples[i] = s16 / (double)short.MaxValue;
            }
        }
        return samples;
    }

    static void ModulateDSB384(double[] input, string outFile)
    {
        //Generate "RF" audio, 384000kps sample rate, 100khz carrier.
        //Simple DSB modulation.

        double[] dsb = new double[input.Length * 8];

        double carrierPhase = 0;
        for (int i = 0; i < input.Length - 1; i++)
        {
            //Linear resampling of input.
            for (int j = 0; j < 8; j++)
            {
                double percentage = j / 8.0;
                double inputSample = input[i] * (1.0 - percentage) + input[i + 1] * percentage;
                double carrier = Math.Sin(carrierPhase);
                carrierPhase += 100000 * 2.0 * Math.PI / 384000;
                //This helps very small errors with double precision - ignore.
                carrierPhase %= 2.0 * Math.PI;
                //Actual DSB modulation, "double balanced mixer".
                dsb[i * 8 + j] = carrier * inputSample;
            }

        }
        SaveAudio(dsb, outFile);
    }

    static void ModulateDSB384Distort(double[] input, string outFile)
    {
        //Generate "RF" audio, 384000kps sample rate, 100khz carrier.
        //Simple DSB modulation.

        double[] dsb = new double[input.Length * 8];

        double carrierPhase = 0;
        for (int i = 0; i < input.Length - 1; i++)
        {
            //Linear resampling of input.
            for (int j = 0; j < 8; j++)
            {
                double percentage = j / 8.0;
                double inputSample = input[i] * (1.0 - percentage) + input[i + 1] * percentage;
                double carrier = Math.Sin(carrierPhase);
                carrierPhase += 50000 * 2.0 * Math.PI / 384000;
                //This helps very small errors with double precision - ignore.
                carrierPhase %= 2.0 * Math.PI;
                //Actual DSB modulation, "double balanced mixer".
                dsb[i * 8 + j] = OverdrivenAmplifier(carrier * inputSample);
            }

        }
        SaveAudio(dsb, outFile);
    }

    static void ModulateDSB384Undistort(double[] input, string outFile)
    {
        //Generate "RF" audio, 384000kps sample rate, 100khz carrier.
        //Simple DSB modulation.

        double[] dsb = new double[input.Length * 8];

        double carrierPhase = 0;
        for (int i = 0; i < input.Length - 1; i++)
        {
            //Linear resampling of input.
            for (int j = 0; j < 8; j++)
            {
                double percentage = j / 8.0;
                double inputSample = input[i] * (1.0 - percentage) + input[i + 1] * percentage;
                double carrier = Math.Sin(carrierPhase);
                carrierPhase += 50000 * 2.0 * Math.PI / 384000;
                //This helps very small errors with double precision - ignore.
                carrierPhase %= 2.0 * Math.PI;
                //Actual DSB modulation, "double balanced mixer".
                dsb[i * 8 + j] = OverdrivenAmplifier(Predistort(carrier * inputSample));
            }

        }
        SaveAudio(dsb, outFile);
    }


    static void ModulateDSB(double[] input, string outFile)
    {
        //Generate "RF" audio, 4.8Msps sample rate, 100khz carrier.
        //Simple DSB modulation.

        double[] dsb = new double[input.Length * 100];

        double carrierPhase = 0;
        for (int i = 0; i < input.Length - 1; i++)
        {
            //Linear resampling of input.
            for (int j = 0; j < 100; j++)
            {
                double percentage = j / 100.0;
                double inputSample = input[i] * (1.0 - percentage) + input[i + 1] * percentage;
                double carrier = Math.Sin(carrierPhase);
                carrierPhase += 50000 * 2.0 * Math.PI / 4800000;
                //This helps very small errors with double precision - ignore.
                carrierPhase %= 2.0 * Math.PI;
                //Actual DSB modulation, "double balanced mixer".
                dsb[i * 100 + j] = carrier * inputSample;
            }

        }
        SaveAudio(dsb, outFile);
    }

    static void PlotFunctions()
    {
        File.Delete("functions.csv");
        using (StreamWriter sw = new StreamWriter("functions.csv"))
        {
            for (int i = 0; i <= 100; i++)
            {
                double percent = i / 100.0;
                sw.WriteLine($"{percent},{LinearAmplifier(percent)},{OverdrivenAmplifier(percent)},{Predistort(percent)},{OverdrivenAmplifier(Predistort(percent))}");
            }
        }
    }
}
