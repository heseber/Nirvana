﻿using System;
using System.IO;
using Compression.FileHandling;
using ErrorHandling;
using Genome;
using VariantAnnotation;
using VariantAnnotation.Interface;
using VariantAnnotation.Interface.AnnotatedPositions;
using VariantAnnotation.Interface.IO;
using VariantAnnotation.Interface.Positions;
using VariantAnnotation.IO;
using VariantAnnotation.IO.VcfWriter;
using VariantAnnotation.Logger;
using VariantAnnotation.Utilities;
using Vcf;



namespace Nirvana
{
    public static class StreamAnnotation
    {
        public static ExitCodes Annotate(Stream headerStream, Stream inputVcfStream, Stream outputJsonStream, Stream outputJsonIndexStream,
            Stream outputVcfStream, Stream outputGvcfStream, AnnotationResources annotationResources, IVcfFilter vcfFilter)
        {

            var logger = outputJsonStream is BlockGZipStream ? new ConsoleLogger() : (ILogger)new NullLogger();
            var metrics = new PerformanceMetrics(logger);
            var vcfConversion = new VcfConversion();

            using (var vcfReader = VcfReader.Create(headerStream, inputVcfStream, annotationResources, vcfFilter))
            using (var jsonWriter = new JsonWriter(outputJsonStream, outputJsonIndexStream, annotationResources, Date.CurrentTimeStamp, vcfReader.GetSampleNames()))
            using (var vcfWriter = annotationResources.OutputVcf
                ? new LiteVcfWriter(new StreamWriter(outputVcfStream), vcfReader.GetHeaderLines(), annotationResources)
                : null)
            using (var gvcfWriter = annotationResources.OutputGvcf
                ? new LiteVcfWriter(new StreamWriter(outputGvcfStream), vcfReader.GetHeaderLines(), annotationResources)
                : null)
            {
                try
                {
                    if (vcfReader.IsRcrsMitochondrion && annotationResources.Annotator.Assembly == GenomeAssembly.GRCh37
                        || annotationResources.Annotator.Assembly == GenomeAssembly.GRCh38
                        || annotationResources.ForceMitochondrialAnnotation)
                        annotationResources.Annotator.EnableMitochondrialAnnotation();

                    int previousChromIndex = -1;
                    IPosition position;

                    while ((position = vcfReader.GetNextPosition()) != null)
                    {
                        if (previousChromIndex != position.Chromosome.Index)
                            annotationResources.PreLoad(position.Chromosome);
                        previousChromIndex = UpdatePerformanceMetrics(previousChromIndex, position.Chromosome, metrics);
                        
                        var annotatedPosition = position.Variants != null ? annotationResources.Annotator.Annotate(position) : null;
                        string json = annotatedPosition?.GetJsonString();

                        if (json != null) WriteOutput(annotatedPosition, jsonWriter, vcfWriter, gvcfWriter, json, vcfConversion);
                        else gvcfWriter?.Write(string.Join("\t", position.VcfFields));

                        metrics.Increment();
                    }

                    jsonWriter.WriteAnnotatedGenes(annotationResources.Annotator.GetGeneAnnotations());
                    
                }
                catch (Exception e)
                {
                    e.Data[ExitCodeUtilities.VcfLine] = vcfReader.VcfLine;
                    throw;
                }
            }

            metrics.ShowAnnotationTime();

            return ExitCodes.Success;
        }

        private static void WriteOutput(IAnnotatedPosition annotatedPosition, IJsonWriter jsonWriter, LiteVcfWriter vcfWriter, LiteVcfWriter gvcfWriter, string jsonOutput, VcfConversion vcfConversion)
        {
            jsonWriter.WriteJsonEntry(annotatedPosition.Position, jsonOutput);

            if (vcfWriter == null && gvcfWriter == null || annotatedPosition.Position.IsRecomposed) return;

            string vcfLine = vcfConversion.Convert(annotatedPosition);
            vcfWriter?.Write(vcfLine);
            gvcfWriter?.Write(vcfLine);
        }


        private static int UpdatePerformanceMetrics(int previousChromIndex, IChromosome chromosome,
            PerformanceMetrics metrics)
        {
            // ReSharper disable once InvertIf
            if (chromosome.Index != previousChromIndex)
            {
                metrics.StartAnnotatingReference(chromosome);
                previousChromIndex = chromosome.Index;
            }

            return previousChromIndex;
        }

    }
}