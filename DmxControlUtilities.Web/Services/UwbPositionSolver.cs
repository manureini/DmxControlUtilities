using System;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;


namespace DmxControlUtilities.Web.Services
{
    public class UwbPositionSolver
    {
        public static Vector<double> CalculateTagPosition(Vector<double>[] anchors, double[] distances)
        {
            if (anchors.Length != 4 || distances.Length != 4)
                throw new ArgumentException("Need exactly 4 anchors and 4 distances");

            var A = Matrix<double>.Build.Dense(3, 3);
            var b = Vector<double>.Build.Dense(3);

            var a0 = anchors[0];
            var d0 = distances[0];

            for (int i = 1; i < 4; i++)
            {
                var ai = anchors[i];

                A[i - 1, 0] = 2 * (ai[0] - a0[0]);
                A[i - 1, 1] = 2 * (ai[1] - a0[1]);
                A[i - 1, 2] = 2 * (ai[2] - a0[2]);

                b[i - 1] =
                    d0 * d0 - distances[i] * distances[i]
                    + ai[0] * ai[0] - a0[0] * a0[0]
                    + ai[1] * ai[1] - a0[1] * a0[1]
                    + ai[2] * ai[2] - a0[2] * a0[2];
            }

            // Solve AX = b
            return A.Solve(b);
        }


        public static Vector<double> CalculatePosition(Vector<double>[] anchors, double[] distances)
        {
            if (anchors.Length < 3)
                throw new ArgumentException("At least 3 anchors are required");

            if (anchors.Length != distances.Length)
                throw new ArgumentException("Anchor and distance count mismatch");

            int rows = anchors.Length - 1;

            var A = Matrix<double>.Build.Dense(rows, 2);
            var b = Vector<double>.Build.Dense(rows);

            var reference = anchors[0];

            for (int i = 1; i < anchors.Length; i++)
            {
                var anchor = anchors[i];

                A[i - 1, 0] =
                    2 * (anchor[0] - reference[0]);

                A[i - 1, 1] =
                    2 * (anchor[1] - reference[1]);


                b[i - 1] =
                    distances[0] * distances[0]
                    - distances[i] * distances[i]
                    + anchor[0] * anchor[0]
                    - reference[0] * reference[0]
                    + anchor[1] * anchor[1]
                    - reference[1] * reference[1];
            }


            // Least squares solution:
            // AX = b
            return A.QR().Solve(b);
        }

    }
}
