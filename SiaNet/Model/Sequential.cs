﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using CNTK;
using SiaNet.Model.Layers;
using System.Data;
using SiaNet.Processing;
using SiaNet.Interface;
using SiaNet.Common;

namespace SiaNet.Model
{
    public delegate void On_Training_Start();

    public delegate void On_Training_End(Dictionary<string, List<double>> trainingResult);

    public delegate void On_Epoch_Start(int epoch);

    public delegate void On_Epoch_End(int epoch, uint samplesSeen, double loss, Dictionary<string, double> metrics);

    public class Sequential : ConfigModule
    {
        public event On_Training_Start OnTrainingStart;

        public event On_Training_End OnTrainingEnd;

        public event On_Epoch_Start OnEpochStart;

        public event On_Epoch_End OnEpochEnd;

        public string Module
        {
            get
            {
                return "Sequential";
            }
        }

        private List<Learner> learners;

        private Function lossFunc;

        private Function metricFunc;

        private Function modelOut;

        private string metricName;

        private string lossName;

        private bool isConvolution;

        private Variable featureVariable;

        private Variable labelVariable;

        private ITrainPredict trainPredict;

        public Dictionary<string, List<double>> TrainingResult { get; set; }

        public List<LayerConfig> Layers { get; set; }

        public Sequential()
        {
            OnTrainingStart += Sequential_OnTrainingStart;
            OnTrainingEnd += Sequential_OnTrainingEnd;
            OnEpochStart += Sequential_OnEpochStart;
            OnEpochEnd += Sequential_OnEpochEnd;
            TrainingResult = new Dictionary<string, List<double>>();
            Layers = new List<LayerConfig>();
            learners = new List<Learner>();
        }

        private void Sequential_OnEpochEnd(int epoch, uint samplesSeen, double loss, Dictionary<string, double> metrics)
        {

        }

        private void Sequential_OnEpochStart(int epoch)
        {

        }

        private void Sequential_OnTrainingEnd(Dictionary<string, List<double>> trainingResult)
        {

        }

        private void Sequential_OnTrainingStart()
        {

        }

        public static Sequential LoadNetConfig(string filepath)
        {
            string json = File.ReadAllText(filepath);
            var result = JsonConvert.DeserializeObject<Sequential>(json);

            return result;
        }

        public void SaveNetConfig(string filepath)
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(filepath, json);
        }

        public void SaveModel(string filepath)
        {
            modelOut.Save(filepath);
        }

        public void LoadModel(string filepath)
        {
            modelOut = Function.Load(filepath, GlobalParameters.Device);
        }

        public void Add(LayerConfig config)
        {
            Layers.Add(config);
        }

        public void Compile(string optimizer, string loss, string metric, Regulizers regulizer = null)
        {
            CompileModel();
            learners.Add(Optimizers.Get(optimizer, modelOut));
            metricName = metric;
            lossName = loss;
            lossFunc = Losses.Get(loss, labelVariable, modelOut);
            metricFunc = Metrics.Get(metric, labelVariable, modelOut);
        }

        public void Compile(Learner optimizer, string loss, string metric, Regulizers regulizer = null)
        {
            CompileModel();
            learners.Add(optimizer);
            metricName = metric;
            lossName = loss;
            lossFunc = Losses.Get(loss, labelVariable, modelOut);
            metricFunc = Metrics.Get(metric, labelVariable, modelOut);
        }

        static Function CreateConvolutionalNeuralNetwork(Variable features, int outDims, DeviceDescriptor device, string classifierName)
        {
            // 28x28x1 -> 14x14x4
            int kernelWidth1 = 3, kernelHeight1 = 3, numInputChannels1 = 1, outFeatureMapCount1 = 4;
            int hStride1 = 2, vStride1 = 2;
            int poolingWindowWidth1 = 3, poolingWindowHeight1 = 3;

            Function pooling1 = ConvolutionWithMaxPooling(features, device, kernelWidth1, kernelHeight1,
                numInputChannels1, outFeatureMapCount1, hStride1, vStride1, poolingWindowWidth1, poolingWindowHeight1);

            // 14x14x4 -> 7x7x8
            int kernelWidth2 = 3, kernelHeight2 = 3, numInputChannels2 = outFeatureMapCount1, outFeatureMapCount2 = 8;
            int hStride2 = 2, vStride2 = 2;
            int poolingWindowWidth2 = 3, poolingWindowHeight2 = 3;

            Function pooling2 = ConvolutionWithMaxPooling(pooling1, device, kernelWidth2, kernelHeight2,
                numInputChannels2, outFeatureMapCount2, hStride2, vStride2, poolingWindowWidth2, poolingWindowHeight2);

            Function denseLayer = Dense(pooling2, outDims, device, classifierName);
            return denseLayer;
        }

        private static Function ConvolutionWithMaxPooling(Variable features, DeviceDescriptor device,
            int kernelWidth, int kernelHeight, int numInputChannels, int outFeatureMapCount,
            int hStride, int vStride, int poolingWindowWidth, int poolingWindowHeight)
        {
            // parameter initialization hyper parameter
            double convWScale = 0.26;
            var convParams = new Parameter(new int[] { kernelWidth, kernelHeight, numInputChannels, outFeatureMapCount }, DataType.Float,
                CNTKLib.GlorotUniformInitializer(convWScale, -1, 2), device);
            Function convFunction = CNTKLib.ReLU(CNTKLib.Convolution(convParams, features, new int[] { 1, 1, numInputChannels } /* strides */));

            Function pooling = CNTKLib.Pooling(convFunction, PoolingType.Max,
                new int[] { poolingWindowWidth, poolingWindowHeight }, new int[] { hStride, vStride }, new bool[] { true });
            return pooling;
        }

        public static Function Dense(Variable input, int outputDim, DeviceDescriptor device, string outputName = "")
        {
            if (input.Shape.Rank != 1)
            {
                // 
                int newDim = input.Shape.Dimensions.Aggregate((d1, d2) => d1 * d2);
                input = CNTKLib.Reshape(input, new int[] { newDim });
            }

            Function fullyConnected = FullyConnectedLinearLayer(input, outputDim, device, outputName);
            return fullyConnected;
        }

        public static Function FullyConnectedLinearLayer(Variable input, int outputDim, DeviceDescriptor device,
            string outputName = "")
        {
            System.Diagnostics.Debug.Assert(input.Shape.Rank == 1);
            int inputDim = input.Shape[0];

            int[] s = { outputDim, inputDim };

            var timesParam = new Parameter((NDShape)s, DataType.Float,
                CNTKLib.GlorotUniformInitializer(
                    CNTKLib.DefaultParamInitScale,
                    CNTKLib.SentinelValueForInferParamInitRank,
                    CNTKLib.SentinelValueForInferParamInitRank, 1),
                device, "timesParam");
            var timesFunction = CNTKLib.Times(timesParam, input, "times");

            int[] s2 = { outputDim };
            var plusParam = new Parameter(s2, 0.0f, device, "plusParam");
            return CNTKLib.Plus(plusParam, timesFunction, outputName);
        }

        private void CompileModel()
        {
            bool first = true;
            foreach (var item in Layers)
            {
                if (first)
                {
                    BuildFirstLayer(item);
                    first = false;
                    continue;
                }

                BuildStackedLayer(item);
            }
            //featureVariable = Variable.InputVariable(new int[] { 28, 28, 1 }, DataType.Float);
            //modelOut =  CreateConvolutionalNeuralNetwork(featureVariable, 10, GlobalParameters.Device, "cls1");
            int outputNums = modelOut.Output.Shape[0];
            labelVariable = Variable.InputVariable(new int[] { outputNums }, DataType.Float);
        }

        private void BuildStackedLayer(LayerConfig layer)
        {
            switch (layer.Name.ToUpper())
            {
                case OptLayers.Dense:
                    var l1 = (Dense)layer;
                    modelOut = NN.Basic.Dense(modelOut, l1.Dim, l1.Act, l1.UseBias, l1.WeightInitializer, l1.BiasInitializer);
                    break;
                case OptLayers.Activation:
                    var l2 = (Activation)layer;
                    modelOut = NN.Basic.Activation(modelOut, l2.Act);
                    break;
                case OptLayers.Dropout:
                    var l3 = (Dropout)layer;
                    modelOut = NN.Basic.Dropout(modelOut, l3.Rate);
                    break;
                case OptLayers.BatchNorm:
                    var l4 = (BatchNorm)layer;
                    modelOut = NN.Basic.BatchNorm(modelOut, l4.Epsilon, l4.BetaInitializer, l4.GammaInitializer, l4.RunningMeanInitializer, l4.RunningStdInvInitializer, l4.Spatial, l4.NormalizationTimeConstant, l4.BlendTimeConst);
                    break;
                case OptLayers.Conv1D:
                    var l5 = (Conv1D)layer;
                    modelOut = NN.Convolution.Conv1D(modelOut, l5.Channels, l5.KernalSize, l5.Strides, l5.Padding, l5.Dialation, l5.Act, l5.UseBias, l5.WeightInitializer, l5.BiasInitializer);
                    break;
                case OptLayers.Conv2D:
                    var l6 = (Conv2D)layer;
                    modelOut = NN.Convolution.Conv2D(modelOut, l6.Channels, l6.KernalSize, l6.Strides, l6.Padding, l6.Dialation, l6.Act, l6.UseBias, l6.WeightInitializer, l6.BiasInitializer);
                    break;
                case OptLayers.Conv3D:
                    var l7 = (Conv3D)layer;
                    modelOut = NN.Convolution.Conv3D(modelOut, l7.Channels, l7.KernalSize, l7.Strides, l7.Padding, l7.Dialation, l7.Act, l7.UseBias, l7.WeightInitializer, l7.BiasInitializer);
                    break;
                case OptLayers.MaxPool1D:
                    var l8 = (MaxPool1D)layer;
                    modelOut = NN.Convolution.MaxPool1D(modelOut, l8.PoolSize, l8.Strides, l8.Padding);
                    break;
                case OptLayers.MaxPool2D:
                    var l9 = (MaxPool2D)layer;
                    modelOut = NN.Convolution.MaxPool2D(modelOut, l9.PoolSize, l9.Strides, l9.Padding);
                    break;
                case OptLayers.MaxPool3D:
                    var l10 = (MaxPool3D)layer;
                    modelOut = NN.Convolution.MaxPool3D(modelOut, l10.PoolSize, l10.Strides, l10.Padding);
                    break;
                case OptLayers.AvgPool1D:
                    var l11 = (AvgPool1D)layer;
                    modelOut = NN.Convolution.AvgPool1D(modelOut, l11.PoolSize, l11.Strides, l11.Padding);
                    break;
                case OptLayers.AvgPool2D:
                    var l12 = (AvgPool2D)layer;
                    modelOut = NN.Convolution.AvgPool2D(modelOut, l12.PoolSize, l12.Strides, l12.Padding);
                    break;
                case OptLayers.AvgPool3D:
                    var l113 = (AvgPool3D)layer;
                    modelOut = NN.Convolution.AvgPool3D(modelOut, l113.PoolSize, l113.Strides, l113.Padding);
                    break;
                case OptLayers.GlobalMaxPool1D:
                    modelOut = NN.Convolution.GlobalMaxPool1D(modelOut);
                    break;
                case OptLayers.GlobalMaxPool2D:
                    modelOut = NN.Convolution.GlobalMaxPool2D(modelOut);
                    break;
                case OptLayers.GlobalMaxPool3D:
                    modelOut = NN.Convolution.GlobalMaxPool3D(modelOut);
                    break;
                case OptLayers.GlobalAvgPool1D:
                    modelOut = NN.Convolution.GlobalAvgPool1D(modelOut);
                    break;
                case OptLayers.GlobalAvgPool2D:
                    modelOut = NN.Convolution.GlobalAvgPool2D(modelOut);
                    break;
                case OptLayers.GlobalAvgPool3D:
                    modelOut = NN.Convolution.GlobalAvgPool3D(modelOut);
                    break;
                default:
                    throw new InvalidOperationException(string.Format("{0} layer is not implemented."));
            }
        }

        private void BuildFirstLayer(LayerConfig layer)
        {
            isConvolution = false;
            switch (layer.Name.ToUpper())
            {
                case OptLayers.Dense:
                    var l1 = (Dense)layer;
                    if (!l1.Shape.HasValue)
                        throw new ArgumentNullException("Input shape is missing for first layer");
                    featureVariable = Variable.InputVariable(new int[] { l1.Shape.Value }, DataType.Float);
                    modelOut = NN.Basic.Dense(featureVariable, l1.Dim, l1.Act, l1.UseBias, l1.WeightInitializer, l1.BiasInitializer);
                    break;
                case OptLayers.BatchNorm:
                    var l2 = (BatchNorm)layer;
                    if (!l2.Shape.HasValue)
                        throw new ArgumentNullException("Input shape is missing for first layer");
                    featureVariable = Variable.InputVariable(new int[] { l2.Shape.Value }, DataType.Float);
                    modelOut = NN.Basic.BatchNorm(featureVariable, l2.Epsilon, l2.BetaInitializer, l2.GammaInitializer, l2.RunningMeanInitializer, l2.RunningStdInvInitializer, l2.Spatial, l2.NormalizationTimeConstant, l2.BlendTimeConst);
                    break;
                case OptLayers.Conv1D:
                    var l3 = (Conv1D)layer;
                    if (l3.Shape == null)
                        throw new ArgumentNullException("Input shape is missing for first layer");
                    featureVariable = Variable.InputVariable(new int[] { l3.Shape.Item1, l3.Shape.Item2 }, DataType.Float);
                    modelOut = NN.Convolution.Conv1D(featureVariable, l3.Channels, l3.KernalSize, l3.Strides, l3.Padding, l3.Dialation, l3.Act, l3.UseBias, l3.WeightInitializer, l3.BiasInitializer);
                    break;
                case OptLayers.Conv2D:
                    var l4 = (Conv2D)layer;
                    if (l4.Shape == null)
                        throw new ArgumentNullException("Input shape is missing for first layer");
                    featureVariable = Variable.InputVariable(new int[] { l4.Shape.Item1, l4.Shape.Item2, l4.Shape.Item3 }, DataType.Float);
                    modelOut = NN.Convolution.Conv2D(featureVariable, l4.Channels, l4.KernalSize, l4.Strides, l4.Padding, l4.Dialation, l4.Act, l4.UseBias, l4.WeightInitializer, l4.BiasInitializer);
                    break;
                case OptLayers.Conv3D:
                    var l5 = (Conv3D)layer;
                    if (l5.Shape == null)
                        throw new ArgumentNullException("Input shape is missing for first layer");
                    featureVariable = Variable.InputVariable(new int[] { l5.Shape.Item1, l5.Shape.Item2, l5.Shape.Item3, l5.Shape.Item4 }, DataType.Float);
                    modelOut = NN.Convolution.Conv3D(featureVariable, l5.Channels, l5.KernalSize, l5.Strides, l5.Padding, l5.Dialation, l5.Act, l5.UseBias, l5.WeightInitializer, l5.BiasInitializer);
                    break;
                default:
                    throw new InvalidOperationException(string.Format("{0} cannot be used as first layer."));
            }
        }

        public void Train(XYFrame train, int epoches, int batchSize, XYFrame validation = null)
        {
            OnTrainingStart();
            trainPredict = new DataFrameTrainPredict(modelOut, lossFunc, lossName, metricFunc, metricName, learners, featureVariable, labelVariable);
            trainPredict.Train(train, validation, epoches, batchSize, OnEpochStart, OnEpochEnd);
            OnTrainingEnd(TrainingResult);
        }

        public void Train(ImageDataGenerator train, int epoches, int batchSize, ImageDataGenerator validation = null)
        {
            OnTrainingStart();
            trainPredict = new ImgGenTrainPredict(modelOut, lossFunc, lossName, metricFunc, metricName, learners, featureVariable, labelVariable);
            trainPredict.Train(train, validation, epoches, batchSize, OnEpochStart, OnEpochEnd);
            OnTrainingEnd(TrainingResult);
        }

        public IList<float> Evaluate(Value data)
        {
            return trainPredict.Evaluate(data);
        }
    }
}