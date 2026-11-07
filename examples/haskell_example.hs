{-# LANGUAGE ForeignFunctionInterface #-}
{-# LANGUAGE BangPatterns #-}

{- |
Module      : Main
Description : ThermoRawFileReader Haskell FFI Example
Copyright   : (c) 2026
License     : MIT

Demonstrates using librawfilereader directly from Haskell via FFI.
Extensively illustrates idiomatic use of 'Either' for operations that can fail,
and 'Maybe' for optional data, queries, and monadic combinators.

Zero external dependencies: only standard GHC 'base'.

Compile & Run:
    ghc -O2 -Llib -lrawfilereader -optl-Wl,-rpath,@loader_path/../lib \
        examples/haskell_example.hs -o examples/haskell_example
    ./examples/haskell_example examples/dda_tmt_example.raw
-}

module Main where

import Control.Exception (bracket)
import Control.Monad (forM, forM_, when)
import Data.Char (isSpace)
import Data.Either (isRight)
import Data.Maybe (catMaybes, fromMaybe, isJust, listToMaybe, mapMaybe)
import Foreign.C.String (CString, peekCString, withCString)
import Foreign.C.Types (CDouble(..), CInt(..))
import Foreign.Marshal.Alloc (alloca)
import Foreign.Marshal.Array (peekArray)
import Foreign.Ptr (Ptr, nullPtr)
import Foreign.Storable (Storable(..))
import System.Environment (getArgs)
import System.Exit (exitFailure)
import Text.Printf (printf)
import Text.Read (readMaybe)

-- ----------------------------------------------------------------------------
-- Error Codes (Mapping to 'Either RawError a')
-- ----------------------------------------------------------------------------

data RawError
  = FileNameEmpty
  | FileNotFound
  | FileOpenFailed
  | FileCorrupt
  | FileUndefined
  | NullPointer
  | InvalidHandle
  | ScanOutOfRange
  | NoData
  | NoCentroidStream
  | NoPrecursorInfo
  | NoInstrument
  | OutOfMemory
  | BufferTooSmall
  | UnexpectedError !CInt
  deriving (Show, Eq)

toRawError :: CInt -> Maybe RawError
toRawError 0   = Nothing
toRawError 101 = Just FileNameEmpty
toRawError 102 = Just FileNotFound
toRawError 103 = Just FileOpenFailed
toRawError 104 = Just FileCorrupt
toRawError 199 = Just FileUndefined
toRawError 201 = Just NullPointer
toRawError 202 = Just InvalidHandle
toRawError 301 = Just ScanOutOfRange
toRawError 302 = Just NoData
toRawError 303 = Just NoCentroidStream
toRawError 304 = Just NoPrecursorInfo
toRawError 305 = Just NoInstrument
toRawError 401 = Just OutOfMemory
toRawError 402 = Just BufferTooSmall
toRawError n   = Just (UnexpectedError n)

-- | Convert a CInt return code to Either RawError ()
checkError :: CInt -> Either RawError ()
checkError 0 = Right ()
checkError n = Left (fromMaybe (UnexpectedError n) (toRawError n))

-- | Either / Maybe conversions
eitherToMaybe :: Either e a -> Maybe a
eitherToMaybe (Right a) = Just a
eitherToMaybe (Left _)  = Nothing

maybeToEither :: e -> Maybe a -> Either e a
maybeToEither _   (Just a) = Right a
maybeToEither err Nothing  = Left err

-- ----------------------------------------------------------------------------
-- C Data Structures (Storable instances)
-- ----------------------------------------------------------------------------

type RawFileHandle = Ptr ()

data RawSpectrumData = RawSpectrumData
  { specMassesPtr      :: !(Ptr CDouble)
  , specIntensitiesPtr :: !(Ptr CDouble)
  , specCount          :: !CInt
  }

instance Storable RawSpectrumData where
  sizeOf _    = 24
  alignment _ = 8
  peek ptr    = do
    m <- peekByteOff ptr 0
    i <- peekByteOff ptr 8
    c <- peekByteOff ptr 16
    return $! RawSpectrumData m i c
  poke ptr (RawSpectrumData m i c) = do
    pokeByteOff ptr 0  m
    pokeByteOff ptr 8  i
    pokeByteOff ptr 16 c

data RawScanStats = RawScanStats
  { scanNumber        :: !CInt
  , scanMsLevel       :: !CInt
  , scanPolarity      :: !CInt
  , scanIsCentroid    :: !CInt
  , scanPacketCount   :: !CInt
  , scanRetentionTime :: !CDouble
  , scanTic           :: !CDouble
  , scanBasePeakMass  :: !CDouble
  , scanBasePeakInt   :: !CDouble
  , scanLowMass       :: !CDouble
  , scanHighMass      :: !CDouble
  } deriving (Show, Eq)

instance Storable RawScanStats where
  sizeOf _    = 72
  alignment _ = 8
  peek ptr    = do
    s  <- peekByteOff ptr 0
    ms <- peekByteOff ptr 4
    pol<- peekByteOff ptr 8
    cen<- peekByteOff ptr 12
    pkt<- peekByteOff ptr 16
    rt <- peekByteOff ptr 24
    tic<- peekByteOff ptr 32
    bpm<- peekByteOff ptr 40
    bpi<- peekByteOff ptr 48
    lm <- peekByteOff ptr 56
    hm <- peekByteOff ptr 64
    return $! RawScanStats s ms pol cen pkt rt tic bpm bpi lm hm
  poke ptr s = do
    pokeByteOff ptr 0  (scanNumber s)
    pokeByteOff ptr 4  (scanMsLevel s)
    pokeByteOff ptr 8  (scanPolarity s)
    pokeByteOff ptr 12 (scanIsCentroid s)
    pokeByteOff ptr 16 (scanPacketCount s)
    pokeByteOff ptr 24 (scanRetentionTime s)
    pokeByteOff ptr 32 (scanTic s)
    pokeByteOff ptr 40 (scanBasePeakMass s)
    pokeByteOff ptr 48 (scanBasePeakInt s)
    pokeByteOff ptr 56 (scanLowMass s)
    pokeByteOff ptr 64 (scanHighMass s)

data RawPrecursorInfo = RawPrecursorInfo
  { precMasterScan   :: !CInt
  , precMass         :: !CDouble
  , precIsolationW   :: !CDouble
  , precChargeState  :: !CInt
  , precMonoMass     :: !CDouble
  , precCollisionEng :: !CDouble
  } deriving (Show, Eq)

instance Storable RawPrecursorInfo where
  sizeOf _    = 48
  alignment _ = 8
  peek ptr    = do
    ms <- peekByteOff ptr 0
    pm <- peekByteOff ptr 8
    iw <- peekByteOff ptr 16
    cs <- peekByteOff ptr 24
    mm <- peekByteOff ptr 32
    ce <- peekByteOff ptr 40
    return $! RawPrecursorInfo ms pm iw cs mm ce
  poke ptr p = do
    pokeByteOff ptr 0  (precMasterScan p)
    pokeByteOff ptr 8  (precMass p)
    pokeByteOff ptr 16 (precIsolationW p)
    pokeByteOff ptr 24 (precChargeState p)
    pokeByteOff ptr 32 (precMonoMass p)
    pokeByteOff ptr 40 (precCollisionEng p)

-- ----------------------------------------------------------------------------
-- FFI Foreign Imports
-- ----------------------------------------------------------------------------

foreign import ccall "raw_open_file"
  c_raw_open_file :: CString -> Ptr RawFileHandle -> IO CInt

foreign import ccall "raw_close_file"
  c_raw_close_file :: RawFileHandle -> IO ()

foreign import ccall "raw_get_first_scan"
  c_raw_get_first_scan :: RawFileHandle -> Ptr CInt -> IO CInt

foreign import ccall "raw_get_last_scan"
  c_raw_get_last_scan :: RawFileHandle -> Ptr CInt -> IO CInt

foreign import ccall "raw_get_spectra_count"
  c_raw_get_spectra_count :: RawFileHandle -> Ptr CInt -> IO CInt

foreign import ccall "raw_get_start_time"
  c_raw_get_start_time :: RawFileHandle -> Ptr CDouble -> IO CInt

foreign import ccall "raw_get_end_time"
  c_raw_get_end_time :: RawFileHandle -> Ptr CDouble -> IO CInt

foreign import ccall "raw_get_instrument_model"
  c_raw_get_instrument_model :: RawFileHandle -> Ptr CString -> IO CInt

foreign import ccall "raw_get_scan_stats"
  c_raw_get_scan_stats :: RawFileHandle -> CInt -> Ptr RawScanStats -> IO CInt

foreign import ccall "raw_get_scan_filter"
  c_raw_get_scan_filter :: RawFileHandle -> CInt -> Ptr CString -> IO CInt

foreign import ccall "raw_get_precursor_info"
  c_raw_get_precursor_info :: RawFileHandle -> CInt -> Ptr RawPrecursorInfo -> IO CInt

foreign import ccall "raw_get_trailer_extra_value_for_label"
  c_raw_get_trailer_extra :: RawFileHandle -> CInt -> CString -> Ptr CString -> IO CInt

foreign import ccall "raw_get_best_spectrum"
  c_raw_get_best_spectrum :: RawFileHandle -> CInt -> Ptr RawSpectrumData -> Ptr CInt -> IO CInt

foreign import ccall "raw_free_string"
  c_raw_free_string :: CString -> IO ()

foreign import ccall "raw_free_spectrum_data"
  c_raw_free_spectrum_data :: Ptr RawSpectrumData -> IO ()

-- ----------------------------------------------------------------------------
-- High-Level Safe Haskell Bindings
-- ----------------------------------------------------------------------------

-- | Bracket-based safe file management: automatically closes the handle.
withRawFile :: FilePath -> (RawFileHandle -> IO (Either RawError a)) -> IO (Either RawError a)
withRawFile path action =
  withCString path $ \cPath ->
    alloca $ \pHandle -> do
      rc <- c_raw_open_file cPath pHandle
      case checkError rc of
        Left err -> return (Left err)
        Right () -> do
          handle <- peek pHandle
          if handle == nullPtr
            then return (Left FileOpenFailed)
            else bracket (return handle) c_raw_close_file action

getFirstScan :: RawFileHandle -> IO (Either RawError Int)
getFirstScan h = alloca $ \p -> do
  rc <- c_raw_get_first_scan h p
  case checkError rc of
    Left err -> return (Left err)
    Right () -> Right . fromIntegral <$> peek p

getLastScan :: RawFileHandle -> IO (Either RawError Int)
getLastScan h = alloca $ \p -> do
  rc <- c_raw_get_last_scan h p
  case checkError rc of
    Left err -> return (Left err)
    Right () -> Right . fromIntegral <$> peek p

getSpectraCount :: RawFileHandle -> IO (Either RawError Int)
getSpectraCount h = alloca $ \p -> do
  rc <- c_raw_get_spectra_count h p
  case checkError rc of
    Left err -> return (Left err)
    Right () -> Right . fromIntegral <$> peek p

getTimeRange :: RawFileHandle -> IO (Either RawError (Double, Double))
getTimeRange h = alloca $ \pStart -> alloca $ \pEnd -> do
  rc1 <- c_raw_get_start_time h pStart
  rc2 <- c_raw_get_end_time h pEnd
  case (checkError rc1, checkError rc2) of
    (Right (), Right ()) -> do
      CDouble s <- peek pStart
      CDouble e <- peek pEnd
      return (Right (s, e))
    (Left e, _) -> return (Left e)
    (_, Left e) -> return (Left e)

getInstrumentModel :: RawFileHandle -> IO (Either RawError String)
getInstrumentModel h = alloca $ \pStr -> do
  rc <- c_raw_get_instrument_model h pStr
  case checkError rc of
    Left err -> return (Left err)
    Right () -> do
      cStr <- peek pStr
      if cStr == nullPtr
        then return (Left NoInstrument)
        else do
          str <- peekCString cStr
          c_raw_free_string cStr
          return (Right str)

getScanStats :: RawFileHandle -> Int -> IO (Either RawError RawScanStats)
getScanStats h scan = alloca $ \pStats -> do
  rc <- c_raw_get_scan_stats h (fromIntegral scan) pStats
  case checkError rc of
    Left err -> return (Left err)
    Right () -> Right <$> peek pStats

getScanFilter :: RawFileHandle -> Int -> IO (Either RawError String)
getScanFilter h scan = alloca $ \pStr -> do
  rc <- c_raw_get_scan_filter h (fromIntegral scan) pStr
  case checkError rc of
    Left err -> return (Left err)
    Right () -> do
      cStr <- peek pStr
      if cStr == nullPtr
        then return (Right "")
        else do
          str <- peekCString cStr
          c_raw_free_string cStr
          return (Right str)

-- | Optional queries returning 'Maybe'
lookupPrecursorInfo :: RawFileHandle -> Int -> IO (Maybe RawPrecursorInfo)
lookupPrecursorInfo h scan = alloca $ \pInfo -> do
  rc <- c_raw_get_precursor_info h (fromIntegral scan) pInfo
  if rc == 0
    then Just <$> peek pInfo
    else return Nothing

lookupTrailerExtra :: RawFileHandle -> Int -> String -> IO (Maybe String)
lookupTrailerExtra h scan label = withCString label $ \cLabel -> alloca $ \pStr -> do
  rc <- c_raw_get_trailer_extra h (fromIntegral scan) cLabel pStr
  if rc == 0
    then do
      cStr <- peek pStr
      if cStr == nullPtr
        then return Nothing
        else do
          str <- peekCString cStr
          c_raw_free_string cStr
          return (Just str)
    else return Nothing

-- | Spectrum representation
data Spectrum = Spectrum
  { spectrumPeaks      :: ![(Double, Double)] -- (m/z, intensity) pairs
  , spectrumIsCentroid :: !Bool
  } deriving (Show, Eq)

getSpectrum :: RawFileHandle -> Int -> IO (Either RawError Spectrum)
getSpectrum h scan = alloca $ \pData -> alloca $ \pCentroid -> do
  rc <- c_raw_get_best_spectrum h (fromIntegral scan) pData pCentroid
  case checkError rc of
    Left err -> return (Left err)
    Right () -> do
      RawSpectrumData mPtr iPtr count <- peek pData
      isCentroid <- peek pCentroid
      let n = fromIntegral count
      masses <- map (\(CDouble m) -> m) <$> peekArray n mPtr
      intensities <- map (\(CDouble i) -> i) <$> peekArray n iPtr
      c_raw_free_spectrum_data pData
      return $ Right $ Spectrum
        { spectrumPeaks      = zip masses intensities
        , spectrumIsCentroid = isCentroid /= 0
        }

-- ----------------------------------------------------------------------------
-- Pure Helpers Utilizing 'Maybe'
-- ----------------------------------------------------------------------------

data Polarity = Positive | Negative deriving (Show, Eq)

polarityFromInt :: CInt -> Maybe Polarity
polarityFromInt 1 = Just Positive
polarityFromInt 2 = Just Negative
polarityFromInt _ = Nothing

optionalCharge :: CInt -> Maybe Int
optionalCharge 0 = Nothing
optionalCharge z = Just (fromIntegral z)

optionalMasterScan :: CInt -> Maybe Int
optionalMasterScan 0 = Nothing
optionalMasterScan s = Just (fromIntegral s)

optionalMass :: CDouble -> Maybe Double
optionalMass (CDouble m) | m <= 0.0  = Nothing
                         | otherwise = Just m

-- | Parse comma-delimited SPS masses from Trailer Extra into Maybe [Double]
parseSpsMasses :: Maybe String -> Maybe [Double]
parseSpsMasses Nothing    = Nothing
parseSpsMasses (Just raw) =
  let tokens = words (map (\c -> if c == ',' then ' ' else c) raw)
      doubles = mapMaybe readMaybe tokens
  in if null doubles then Nothing else Just doubles

-- | Find first scan matching a given MS level using scan stats and Maybe
findFirstScanOfLevel :: RawFileHandle -> Int -> Int -> Int -> IO (Maybe Int)
findFirstScanOfLevel h targetLevel cur lastScan
  | cur > lastScan = return Nothing
  | otherwise = do
      res <- getScanStats h cur
      case res of
        Right stats | scanMsLevel stats == fromIntegral targetLevel -> return (Just cur)
        _ -> findFirstScanOfLevel h targetLevel (cur + 1) lastScan

-- ----------------------------------------------------------------------------
-- Main Application
-- ----------------------------------------------------------------------------


main :: IO ()
main = do
  args <- getArgs
  let rawPath = case listToMaybe args of
        Just p  -> p
        Nothing -> "examples/dda_tmt_example.raw"

  putStrLn "===================================================="
  putStrLn "   ThermoRawFileReader Haskell FFI Demonstration    "
  putStrLn "===================================================="
  printf "Opening file: %s\n\n" rawPath

  result <- withRawFile rawPath $ \h -> do
    -- 1. Read file-level metadata via Either
    eFirstScan <- getFirstScan h
    eLastScan  <- getLastScan h
    eCount     <- getSpectraCount h
    eTimeRange <- getTimeRange h
    eModel     <- getInstrumentModel h

    -- Monadic 'Either' composition for file summary
    case (,,,,) <$> eFirstScan <*> eLastScan <*> eCount <*> eTimeRange <*> eModel of
      Left err -> return (Left err)
      Right (firstScan, lastScan, count, (startTime, endTime), model) -> do
        printf "File Information:\n"
        printf "  Instrument:  %s\n" model
        printf "  Scan Range:  %d - %d (Total: %d spectra)\n" firstScan lastScan count
        printf "  Time Range:  %.2f - %.2f min\n\n" startTime endTime

        -- 2. Locate first MS1, MS2, and MS3 scans using 'Maybe'
        putStrLn "Locating Representative Scans for MS1, MS2, MS3..."
        mMs1Scan <- findFirstScanOfLevel h 1 firstScan lastScan
        mMs2Scan <- findFirstScanOfLevel h 2 firstScan lastScan
        mMs3Scan <- findFirstScanOfLevel h 3 firstScan lastScan

        let levelScans = catMaybes
              [ (,) (1 :: Int) <$> mMs1Scan
              , (,) (2 :: Int) <$> mMs2Scan
              , (,) (3 :: Int) <$> mMs3Scan
              ]

        -- 3. Detailed inspection of each scan level
        forM_ levelScans $ \(level, scanNum) -> do
          printf "----------------------------------------------------\n"
          printf "=== MS%d Scan (Scan Number %d) ===\n" level scanNum
          printf "----------------------------------------------------\n"

          -- Scan stats via Either
          eStats <- getScanStats h scanNum
          case eStats of
            Left err -> printf "  [!] Error fetching stats: %s\n" (show err)
            Right stats -> do
              let CDouble rt  = scanRetentionTime stats
                  CDouble tic = scanTic stats
                  mPol = polarityFromInt (scanPolarity stats)
                  polStr = case mPol of
                    Just Positive -> "(+)"
                    Just Negative -> "(-)"
                    Nothing       -> "(?)"
              printf "  Retention Time: %.3f min\n" rt
              printf "  TIC:            %.2e\n" tic
              printf "  Polarity:       %s\n" polStr

          -- Filter string via Either
          eFilter <- getScanFilter h scanNum
          case eFilter of
            Right f | not (null f) -> printf "  Filter:         %s\n" f
            _                      -> return ()

          -- Precursor Info via Maybe
          mPrec <- lookupPrecursorInfo h scanNum
          case mPrec of
            Nothing -> printf "  Precursor:      None (Single MS / unfragmented)\n"
            Just prec -> do
              let CDouble pm = precMass prec
                  CDouble iw = precIsolationW prec
                  mCharge    = optionalCharge (precChargeState prec)
                  mMaster    = optionalMasterScan (precMasterScan prec)
                  chargeStr  = maybe "unknown" (\z -> printf "+%d" z) mCharge
                  masterStr  = maybe "none" show mMaster

              printf "  Precursor m/z:  %.4f (isolation width: %.2f, charge: %s)\n" pm iw chargeStr
              printf "  Master Scan:    %s\n" masterStr

          -- SPS Masses (MS3 TMT Quantification) via Maybe parsing
          when (level == 3) $ do
            mSpsRaw <- lookupTrailerExtra h scanNum "SPS Masses"
            case parseSpsMasses mSpsRaw of
              Nothing -> return ()
              Just notches -> do
                printf "  SPS Notches:    %d notches detected\n" (length notches)
                printf "    Values:       %s\n" (unwords (map (printf "%.2f") notches))

          -- Spectrum Data via Either
          eSpec <- getSpectrum h scanNum
          case eSpec of
            Left err -> printf "  [!] Error fetching spectrum: %s\n" (show err)
            Right spec -> do
              let peaks = spectrumPeaks spec
                  modeStr = if spectrumIsCentroid spec then "Centroid" else "Profile"
                  peakCount = length peaks
              printf "  Spectrum:       %d peaks (%s)\n" peakCount modeStr

              -- Show reporter ions for TMT MS3
              when (level == 3) $ do
                let tmtPeaks = filter (\(mz, _) -> mz >= 126.0 && mz <= 135.0) peaks
                when (not (null tmtPeaks)) $ do
                  printf "  TMT Reporter Ions (126 - 135 m/z):\n"
                  forM_ (take 8 tmtPeaks) $ \(mz, intensity) ->
                    printf "    m/z %8.4f : intensity %10.1f\n" mz intensity

          putStrLn ""

        return (Right ())

  case result of
    Left err -> do
      printf "Operation failed with error: %s\n" (show err)
      exitFailure
    Right () ->
      putStrLn "File closed cleanly. Haskell demonstration completed successfully."
